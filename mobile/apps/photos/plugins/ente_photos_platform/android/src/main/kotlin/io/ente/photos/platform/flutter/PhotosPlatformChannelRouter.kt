package io.ente.photos.platform.flutter

import io.flutter.embedding.engine.plugins.FlutterPlugin
import io.flutter.plugin.common.MethodCall
import io.flutter.plugin.common.MethodChannel

internal class PhotosPlatformChannelRouter : MethodChannel.MethodCallHandler {
    private val healthAdapter = DeviceHealthChannelAdapter()
    private val startupDiagnosticsAdapter = StartupDiagnosticsChannelAdapter()
    private val trashAdapter = DeviceTrashChannelAdapter()
    private lateinit var methodChannel: MethodChannel

    fun attach(binding: FlutterPlugin.FlutterPluginBinding) {
        healthAdapter.attach(binding)
        startupDiagnosticsAdapter.attach(binding)
        trashAdapter.attach(binding)
        methodChannel = MethodChannel(binding.binaryMessenger, METHOD_CHANNEL)
        methodChannel.setMethodCallHandler(this)
    }

    override fun onMethodCall(call: MethodCall, result: MethodChannel.Result) {
        when {
            call.method.startsWith("deviceHealth.") -> healthAdapter.onMethodCall(call, result)

            call.method.startsWith("deviceTrash.") -> trashAdapter.onMethodCall(call, result)

            call.method.startsWith("startupDiagnostics.") ->
                startupDiagnosticsAdapter.onMethodCall(call, result)

            else -> result.notImplemented()
        }
    }

    fun detach() {
        methodChannel.setMethodCallHandler(null)
        healthAdapter.detach()
        startupDiagnosticsAdapter.detach()
        trashAdapter.detach()
    }

    private companion object {
        const val METHOD_CHANNEL = "io.ente.photos.platform"
    }
}
