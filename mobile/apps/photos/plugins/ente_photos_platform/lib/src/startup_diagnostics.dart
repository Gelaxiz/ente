import 'dart:io';

import 'package:flutter/services.dart';

/// Persists a small, privacy-safe startup timeline outside the normal logger.
///
/// The Android implementation writes directly into the exported logs
/// directory, so markers emitted before the Dart logger is ready survive a
/// process kill. Other platforms intentionally do nothing.
class StartupDiagnosticsClient {
  StartupDiagnosticsClient({MethodChannel? methodChannel, bool? isAndroid})
    : _methodChannel = methodChannel ?? const MethodChannel(_channelName),
      _isAndroid = isAndroid ?? Platform.isAndroid;

  static final instance = StartupDiagnosticsClient();
  static const _channelName = 'io.ente.photos.platform';

  final MethodChannel _methodChannel;
  final bool _isAndroid;

  Future<void> mark(
    String event, {
    required String role,
    Map<String, Object?> details = const {},
  }) async {
    if (!_isAndroid) return;
    try {
      await _methodChannel.invokeMethod<void>('startupDiagnostics.mark', {
        'event': event,
        'role': role,
        'details': details,
      });
    } catch (_) {
      // Diagnostics must never affect startup behavior.
    }
  }
}
