package io.ente.photos.platform.startup

import android.content.Context
import android.os.Process
import android.os.SystemClock
import android.util.Log
import org.json.JSONObject
import java.io.File
import java.io.FileOutputStream
import java.util.concurrent.Executors
import java.util.concurrent.atomic.AtomicLong

/**
 * A small append-only startup journal that does not depend on Dart logging.
 *
 * The file lives inside the existing logs directory, so the normal "Export
 * logs" flow includes it. Events intentionally contain only technical stage
 * names, process/engine identifiers, and timestamps.
 */
object StartupDiagnosticsJournal {
    private const val TAG = "StartupDiagnostics"
    private const val VERSION = 1
    private const val LOG_DIRECTORY = "logs"
    private const val LOG_FILE = "startup-diagnostics.log"
    private const val MAX_FILE_BYTES = 256 * 1024L
    private const val MAX_RETAINED_LINES = 384

    private val lock = Any()
    private val attemptSequence = AtomicLong(0)
    private val writer = Executors.newSingleThreadExecutor { runnable ->
        Thread(runnable, "ente-startup-diagnostics").apply {
            isDaemon = true
        }
    }

    @Volatile
    private var sessionId: String? = null

    @Volatile
    private var currentAttemptId: String? = null

    fun initialize(context: Context) {
        synchronized(lock) {
            if (sessionId != null) return
            sessionId = "${System.currentTimeMillis()}-${Process.myPid()}"
            enqueueAppendLocked(context, "process.application.create", "native")
        }
    }

    fun beginForegroundAttempt(context: Context, trigger: String): String {
        synchronized(lock) {
            ensureInitializedLocked(context)
            val attempt =
                "${sessionId}-fg-${attemptSequence.incrementAndGet()}"
            currentAttemptId = attempt
            enqueueAppendLocked(
                context,
                "foreground.attempt.begin",
                "native",
                attempt,
                mapOf("trigger" to trigger),
            )
            return attempt
        }
    }

    fun endForegroundAttempt(
        context: Context,
        attemptId: String?,
        displayed: Boolean,
        reason: String,
    ) {
        if (attemptId == null) return
        synchronized(lock) {
            ensureInitializedLocked(context)
            enqueueAppendLocked(
                context,
                "foreground.attempt.end",
                "native",
                attemptId,
                mapOf(
                    "displayed" to displayed.toString(),
                    "reason" to reason,
                ),
            )
            if (currentAttemptId == attemptId) {
                currentAttemptId = null
            }
        }
    }

    fun mark(
        context: Context,
        event: String,
        role: String,
        attemptId: String? = currentAttemptId,
        details: Map<String, String> = emptyMap(),
    ) {
        synchronized(lock) {
            ensureInitializedLocked(context)
            enqueueAppendLocked(context, event, role, attemptId, details)
        }
    }

    private fun ensureInitializedLocked(context: Context) {
        if (sessionId != null) return
        sessionId = "${System.currentTimeMillis()}-${Process.myPid()}"
        enqueueAppendLocked(context, "process.application.create.late", "native")
    }

    private fun enqueueAppendLocked(
        context: Context,
        event: String,
        role: String,
        attemptId: String? = currentAttemptId,
        details: Map<String, String> = emptyMap(),
    ) {
        val applicationContext = context.applicationContext
        val eventSessionId = sessionId
        val eventDetails = details.toMap()
        val wallTimeMs = System.currentTimeMillis()
        val elapsedTimeMs = SystemClock.elapsedRealtime()
        val processId = Process.myPid()
        val threadId = Process.myTid()
        writer.execute {
            append(
                applicationContext,
                event,
                role,
                eventSessionId,
                attemptId,
                eventDetails,
                wallTimeMs,
                elapsedTimeMs,
                processId,
                threadId,
            )
        }
    }

    private fun append(
        context: Context,
        event: String,
        role: String,
        eventSessionId: String?,
        attemptId: String?,
        details: Map<String, String>,
        wallTimeMs: Long,
        elapsedTimeMs: Long,
        processId: Int,
        threadId: Int,
    ) {
        runCatching {
            val payload = JSONObject().apply {
                put("v", VERSION)
                put("wall_ms", wallTimeMs)
                put("elapsed_ms", elapsedTimeMs)
                put("pid", processId)
                put("tid", threadId)
                put("session_id", eventSessionId)
                put("attempt_id", attemptId ?: JSONObject.NULL)
                put("role", role)
                put("event", event)
                if (details.isNotEmpty()) {
                    put("details", JSONObject(details))
                }
            }
            val file = logFile(context)
            file.parentFile?.mkdirs()
            FileOutputStream(file, true).bufferedWriter(Charsets.UTF_8).use {
                it.appendLine(payload.toString())
            }
            trimIfNeeded(file)
        }.onFailure {
            Log.w(TAG, "Failed to persist startup diagnostic marker", it)
        }
    }

    private fun logFile(context: Context): File =
        File(File(context.filesDir, LOG_DIRECTORY), LOG_FILE)

    private fun trimIfNeeded(file: File) {
        if (!file.exists() || file.length() <= MAX_FILE_BYTES) return
        val retained = runCatching {
            file.useLines { lines -> lines.toList().takeLast(MAX_RETAINED_LINES) }
        }.getOrElse {
            Log.w(TAG, "Failed to trim startup diagnostics", it)
            emptyList()
        }
        rewrite(file, retained)
    }

    private fun rewrite(file: File, lines: List<String>) {
        runCatching {
            file.parentFile?.mkdirs()
            val temporary = File(file.parentFile, "$LOG_FILE.tmp")
            temporary.bufferedWriter(Charsets.UTF_8).use { writer ->
                lines.forEach { writer.appendLine(it) }
            }
            if (!temporary.renameTo(file)) {
                file.writeText(temporary.readText(Charsets.UTF_8), Charsets.UTF_8)
                temporary.delete()
            }
        }.onFailure {
            Log.w(TAG, "Failed to rewrite startup diagnostics", it)
        }
    }
}
