import 'package:ente_photos_platform/ente_photos_platform.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();

  const channel = MethodChannel('io.ente.photos.platform/test-startup');
  final messenger =
      TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger;

  tearDown(() => messenger.setMockMethodCallHandler(channel, null));

  test('persists a startup marker on Android', () async {
    MethodCall? receivedCall;
    messenger.setMockMethodCallHandler(channel, (call) async {
      receivedCall = call;
      return null;
    });
    final client = StartupDiagnosticsClient(
      methodChannel: channel,
      isAndroid: true,
    );

    await client.mark(
      'dart.run_app.before',
      role: 'fg',
      details: const {'attempt': 3},
    );

    expect(receivedCall?.method, 'startupDiagnostics.mark');
    expect(receivedCall?.arguments, {
      'event': 'dart.run_app.before',
      'role': 'fg',
      'details': {'attempt': 3},
    });
  });

  test('does not invoke the channel on other platforms', () async {
    var invoked = false;
    messenger.setMockMethodCallHandler(channel, (call) async {
      invoked = true;
      return null;
    });
    final client = StartupDiagnosticsClient(
      methodChannel: channel,
      isAndroid: false,
    );

    await client.mark('dart.binding.ready', role: 'fg');
    expect(invoked, isFalse);
  });

  test('diagnostic channel failures never affect startup', () async {
    messenger.setMockMethodCallHandler(channel, (call) async {
      throw StateError('diagnostic channel unavailable');
    });
    final client = StartupDiagnosticsClient(
      methodChannel: channel,
      isAndroid: true,
    );

    await expectLater(client.mark('dart.binding.ready', role: 'fg'), completes);
  });
}
