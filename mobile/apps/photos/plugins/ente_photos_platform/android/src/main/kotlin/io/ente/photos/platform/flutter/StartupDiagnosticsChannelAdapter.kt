package io.ente.photos.platform.flutter

import android.content.Context
import io.ente.photos.platform.startup.StartupDiagnosticsJournal
import io.flutter.embedding.engine.plugins.FlutterPlugin
import io.flutter.plugin.common.MethodCall
import io.flutter.plugin.common.MethodChannel

internal class StartupDiagnosticsChannelAdapter {
    private var context: Context? = null

    fun attach(binding: FlutterPlugin.FlutterPluginBinding) {
        context = binding.applicationContext
    }

    fun onMethodCall(call: MethodCall, result: MethodChannel.Result) {
        val applicationContext = context
        if (applicationContext == null) {
            result.error("unavailable", "Startup diagnostics are detached", null)
            return
        }
        when (call.method) {
            "startupDiagnostics.mark" -> mark(applicationContext, call, result)
            else -> result.notImplemented()
        }
    }

    fun detach() {
        context = null
    }

    private fun mark(
        context: Context,
        call: MethodCall,
        result: MethodChannel.Result,
    ) {
        val event = call.argument<String>("event")
        val role = call.argument<String>("role")
        if (event.isNullOrBlank() || role.isNullOrBlank()) {
            result.error("invalid_arguments", "event and role are required", null)
            return
        }
        val rawDetails = call.argument<Map<*, *>>("details") ?: emptyMap<Any, Any>()
        val details = rawDetails.entries.associate { (key, value) ->
            key.toString() to value.toString()
        }
        StartupDiagnosticsJournal.mark(context, event, role, details = details)
        result.success(null)
    }
}
