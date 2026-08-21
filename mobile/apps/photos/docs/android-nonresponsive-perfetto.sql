-- Perfetto analysis for the Android On-this-day notification black-screen
-- reproduction.
-- Run with a current trace_processor against one trial trace at a time.
-- For a non-Play application ID or a different foreground layer name, make a
-- per-experiment copy, update repro_params in both query blocks, hash it, and
-- freeze that copy across every compared trial.

INCLUDE PERFETTO MODULE android.frames.timeline;

-- Gate 1: this query must return zero rows.
SELECT *
FROM stats
WHERE severity = 'data_loss' AND value != 0;

-- Gate 2: source preflight. Run one Home/Albums/Home/swipe interaction during
-- the preflight trace. Every count must be nonzero. The counter count verifies
-- that polling sources produced data; inspect the following track-name query
-- to confirm memory/system counters expected from the target OEM build.
WITH
  repro_params AS (
    SELECT
      'io.ente.photos' AS package_name,
      '*io.ente.photos*' AS layer_glob
  )
SELECT 'package_sched_rows' AS signal, COUNT(*) AS row_count
FROM sched s
JOIN thread t USING (utid)
JOIN process p USING (upid)
JOIN repro_params r ON p.name = r.package_name
UNION ALL
SELECT 'foreground_frame_rows', COUNT(*)
FROM actual_frame_timeline_slice a
JOIN process p USING (upid)
JOIN repro_params r
  ON p.name = r.package_name AND a.layer_name GLOB r.layer_glob
UNION ALL
SELECT 'repro_android_log_rows', COUNT(*)
FROM android_logs
WHERE tag = 'ENTE_REPRO'
UNION ALL
SELECT 'process_rows', COUNT(*)
FROM process p
JOIN repro_params r ON p.name = r.package_name
UNION ALL
SELECT 'counter_rows', COUNT(*)
FROM counter;

SELECT DISTINCT name
FROM counter_track
ORDER BY name;

-- Gate 3: list the actual foreground layers. The frozen layer_glob used below
-- must select the Ente MainActivity surface and no unrelated surface.
SELECT
  p.name AS process_name,
  a.layer_name,
  COUNT(*) AS frame_count
FROM actual_frame_timeline_slice a
JOIN process p USING (upid)
GROUP BY p.name, a.layer_name
ORDER BY frame_count DESC;

-- Score 1: app-scoped frame distribution inside the marker-bounded window.
-- Nearest-rank p95 is calculated over all selected actual frame durations.
WITH
  repro_params AS (
    SELECT
      'io.ente.photos' AS package_name,
      '*io.ente.photos*' AS layer_glob
  ),
  bounds AS (
    SELECT
      MIN(CASE WHEN msg GLOB 'SCORED_WINDOW_START*' THEN ts END) AS start_ts,
      MAX(CASE WHEN msg GLOB 'SCORED_WINDOW_END*' THEN ts END) AS end_ts
    FROM android_logs
    WHERE tag = 'ENTE_REPRO'
  ),
  frames AS (
    SELECT
      a.ts,
      a.dur,
      a.ts + a.dur AS presented_ts
    FROM actual_frame_timeline_slice a
    JOIN process p USING (upid)
    JOIN repro_params r
      ON p.name = r.package_name AND a.layer_name GLOB r.layer_glob
    JOIN bounds b
      ON a.ts < b.end_ts AND a.ts + a.dur >= b.start_ts
  ),
  ranked AS (
    SELECT
      dur / 1000000.0 AS frame_ms,
      ROW_NUMBER() OVER (ORDER BY dur) AS rank_number,
      COUNT(*) OVER () AS frame_count
    FROM frames
  )
SELECT
  MAX(frame_count) AS frame_count,
  ROUND(
    MIN(
      CASE
        WHEN rank_number * 100 >= frame_count * 95 THEN frame_ms
      END
    ),
    3
  ) AS p95_frame_ms,
  ROUND(MAX(frame_ms), 3) AS max_frozen_frame_ms
FROM ranked;

-- Score 2: pair each input with its on-device UI state marker and first Ente
-- frame whose lifecycle began after the input marker. This is first app-frame
-- latency, not proof that pixels visibly changed; use the screen recording for
-- input_to_first_changed_frame_ms.
WITH
  repro_params AS (
    SELECT
      'io.ente.photos' AS package_name,
      '*io.ente.photos*' AS layer_glob
  ),
  bounds AS (
    SELECT
      MIN(CASE WHEN msg GLOB 'SCORED_WINDOW_START*' THEN ts END) AS start_ts,
      MAX(CASE WHEN msg GLOB 'SCORED_WINDOW_END*' THEN ts END) AS end_ts
    FROM android_logs
    WHERE tag = 'ENTE_REPRO'
  ),
  frames AS (
    SELECT
      a.ts,
      a.ts + a.dur AS presented_ts
    FROM actual_frame_timeline_slice a
    JOIN process p USING (upid)
    JOIN repro_params r
      ON p.name = r.package_name AND a.layer_name GLOB r.layer_glob
    JOIN bounds b
      ON a.ts BETWEEN b.start_ts AND b.end_ts
  ),
  inputs AS (
    SELECT
      CAST(substr(msg, instr(msg, 'seq=') + 4, 2) AS INT)
        AS sequence_number,
      ts AS input_ts,
      msg AS input_marker
    FROM android_logs
    WHERE tag = 'ENTE_REPRO' AND msg GLOB 'INPUT_BEGIN*'
  ),
  states AS (
    SELECT
      CAST(substr(msg, instr(msg, 'seq=') + 4, 2) AS INT)
        AS sequence_number,
      ts AS state_ts,
      msg AS state_marker
    FROM android_logs
    WHERE tag = 'ENTE_REPRO' AND msg GLOB 'STATE_REACHED*'
  )
SELECT
  i.sequence_number,
  i.input_marker,
  s.state_marker,
  ROUND((s.state_ts - i.input_ts) / 1000000.0, 3)
    AS input_to_expected_state_ms,
  ROUND(
    (
      SELECT MIN(f.presented_ts)
      FROM frames f
      WHERE f.ts >= i.input_ts
    ) / 1000000.0 - i.input_ts / 1000000.0,
    3
  ) AS input_to_first_app_frame_ms
FROM inputs i
LEFT JOIN states s USING (sequence_number)
ORDER BY i.sequence_number;
