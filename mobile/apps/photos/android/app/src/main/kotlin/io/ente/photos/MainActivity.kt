package io.ente.photos

import android.content.Intent
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import io.ente.photos.platform.startup.StartupDiagnosticsJournal
import io.flutter.embedding.android.FlutterFragmentActivity
import io.flutter.embedding.engine.FlutterEngine
import io.flutter.embedding.engine.renderer.FlutterUiDisplayListener

class MainActivity : FlutterFragmentActivity() {
    private val mainHandler = Handler(Looper.getMainLooper())
    private var configuredEngine: FlutterEngine? = null
    private var attemptId: String? = null
    private var flutterUiDisplayedThisAttempt = false
    private var flutterUiCurrentlyDisplayed = false
    private var shortNoUiWatchdog: Runnable? = null
    private var longNoUiWatchdog: Runnable? = null

    private val flutterUiDisplayListener = object : FlutterUiDisplayListener {
        override fun onFlutterUiDisplayed() {
            flutterUiDisplayedThisAttempt = true
            flutterUiCurrentlyDisplayed = true
            cancelNoUiWatchdogs()
            marker("flutter_ui.displayed", configuredEngine)
        }

        override fun onFlutterUiNoLongerDisplayed() {
            flutterUiCurrentlyDisplayed = false
            marker("flutter_ui.hidden", configuredEngine)
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        ensureAttempt("activity_create")
        marker("activity.onCreate.before")
        super.onCreate(savedInstanceState)
        marker("activity.onCreate.after")
    }

    override fun configureFlutterEngine(flutterEngine: FlutterEngine) {
        marker("activity.configure_engine.before", flutterEngine)
        super.configureFlutterEngine(flutterEngine)
        configuredEngine = flutterEngine
        flutterEngine.renderer.addIsDisplayingFlutterUiListener(
            flutterUiDisplayListener,
        )
        marker("activity.configure_engine.after", flutterEngine)
    }

    override fun cleanUpFlutterEngine(flutterEngine: FlutterEngine) {
        marker("activity.cleanup_engine.before", flutterEngine)
        flutterEngine.renderer.removeIsDisplayingFlutterUiListener(
            flutterUiDisplayListener,
        )
        configuredEngine = null
        super.cleanUpFlutterEngine(flutterEngine)
        marker("activity.cleanup_engine.after", flutterEngine)
    }

    override fun onNewIntent(intent: Intent) {
        ensureAttempt("new_intent")
        marker("activity.onNewIntent.before")
        setIntent(intent)
        super.onNewIntent(intent)
        marker("activity.onNewIntent.after")
    }

    override fun onStart() {
        ensureAttempt("activity_start")
        marker("activity.onStart.before")
        super.onStart()
        marker("activity.onStart.after")
    }

    override fun onResume() {
        marker("activity.onResume.before")
        super.onResume()
        marker("activity.onResume.after")
        scheduleNoUiWatchdogs()
    }

    override fun onPostResume() {
        marker("activity.onPostResume.before")
        super.onPostResume()
        marker("activity.onPostResume.after")
    }

    override fun onPause() {
        marker("activity.onPause.before")
        super.onPause()
        marker("activity.onPause.after")
    }

    override fun onStop() {
        cancelNoUiWatchdogs()
        marker("activity.onStop.before")
        super.onStop()
        marker("activity.onStop.after")
        StartupDiagnosticsJournal.endForegroundAttempt(
            this,
            attemptId,
            flutterUiDisplayedThisAttempt,
            "activity_stop",
        )
        attemptId = null
    }

    override fun onDestroy() {
        cancelNoUiWatchdogs()
        marker("activity.onDestroy.before")
        super.onDestroy()
        marker("activity.onDestroy.after")
        StartupDiagnosticsJournal.endForegroundAttempt(
            this,
            attemptId,
            flutterUiDisplayedThisAttempt,
            "activity_destroy",
        )
        attemptId = null
    }

    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)
        marker(
            "activity.window_focus",
            details = mapOf("has_focus" to hasFocus.toString()),
        )
    }

    private fun ensureAttempt(trigger: String) {
        if (attemptId != null) return
        flutterUiDisplayedThisAttempt = false
        flutterUiCurrentlyDisplayed =
            configuredEngine?.renderer?.isDisplayingFlutterUi == true
        attemptId = StartupDiagnosticsJournal.beginForegroundAttempt(this, trigger)
    }

    private fun scheduleNoUiWatchdogs() {
        cancelNoUiWatchdogs()
        val watchedAttempt = attemptId
        val shortWatchdog = Runnable {
            markNoUiIfNeeded(watchedAttempt, "activity.no_flutter_ui_3s")
        }
        val longWatchdog = Runnable {
            markNoUiIfNeeded(watchedAttempt, "activity.no_flutter_ui_10s")
        }
        shortNoUiWatchdog = shortWatchdog
        longNoUiWatchdog = longWatchdog
        mainHandler.postDelayed(shortWatchdog, NO_UI_SHORT_TIMEOUT_MS)
        mainHandler.postDelayed(longWatchdog, NO_UI_LONG_TIMEOUT_MS)
    }

    private fun cancelNoUiWatchdogs() {
        shortNoUiWatchdog?.let(mainHandler::removeCallbacks)
        longNoUiWatchdog?.let(mainHandler::removeCallbacks)
        shortNoUiWatchdog = null
        longNoUiWatchdog = null
    }

    private fun markNoUiIfNeeded(watchedAttempt: String?, event: String) {
        if (
            watchedAttempt == null ||
            watchedAttempt != attemptId ||
            flutterUiCurrentlyDisplayed
        ) {
            return
        }
        marker(
            event,
            details = mapOf(
                "has_focus" to hasWindowFocus().toString(),
                "decor_shown" to window.decorView.isShown.toString(),
                "finishing" to isFinishing.toString(),
            ),
        )
    }

    private fun marker(
        event: String,
        engine: FlutterEngine? = configuredEngine,
        details: Map<String, String> = emptyMap(),
    ) {
        val markerDetails = details.toMutableMap().apply {
            put("engine", engine?.let { System.identityHashCode(it) }.toString())
        }
        StartupDiagnosticsJournal.mark(
            this,
            event,
            "native_fg",
            attemptId,
            markerDetails,
        )
    }

    private companion object {
        const val NO_UI_SHORT_TIMEOUT_MS = 3_000L
        const val NO_UI_LONG_TIMEOUT_MS = 10_000L
    }
}
