import 'dart:async';

import 'package:ente_lock_screen/lock_screen_host.dart';
import 'package:ente_lock_screen/lock_screen_settings.dart';
import 'package:ente_lock_screen/ui/app_lock.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

void main() {
  setUpAll(() async {
    SharedPreferences.setMockInitialValues({
      LockScreenSettings.keyHasMigratedLockScreenChanges: true,
      LockScreenSettings.autoLockTime: 5000,
    });
    await LockScreenSettings.instance.init(_TestLockScreenHost());
  });

  testWidgets('updates theme mode after startup', (tester) async {
    await tester.pumpWidget(
      _buildAppLock(
        ThemeMode.dark,
        child: Builder(
          builder: (context) => Column(
            children: [
              Text(Theme.of(context).brightness.name),
              GestureDetector(
                behavior: HitTestBehavior.opaque,
                onTap: () => AppLock.of(context)!.setThemeMode(ThemeMode.light),
                child: const Text('Use light'),
              ),
            ],
          ),
        ),
      ),
    );

    expect(find.text('dark'), findsOneWidget);

    await tester.tap(find.text('Use light'));
    await tester.pumpAndSettle();

    expect(find.text('light'), findsOneWidget);
  });

  testWidgets('syncs theme mode when savedThemeMode changes', (tester) async {
    await tester.pumpWidget(_buildAppLock(ThemeMode.dark));

    expect(find.text('dark'), findsOneWidget);

    await tester.pumpWidget(_buildAppLock(ThemeMode.light));
    await tester.pumpAndSettle();

    expect(find.text('light'), findsOneWidget);
  });

  testWidgets('can hide the debug banner', (tester) async {
    await tester.pumpWidget(
      _buildAppLock(ThemeMode.system, debugShowCheckedModeBanner: false),
    );

    final materialApp = tester.widget<MaterialApp>(find.byType(MaterialApp));
    expect(materialApp.debugShowCheckedModeBanner, isFalse);
  });

  testWidgets('covers unlocked content until unlock', (tester) async {
    await tester.pumpWidget(
      _buildAppLock(
        ThemeMode.light,
        child: Builder(
          builder: (context) => GestureDetector(
            behavior: HitTestBehavior.opaque,
            onTap: () => AppLock.of(context)!.showLockScreen(),
            child: const Text('secret content'),
          ),
        ),
        lockScreen: Builder(
          builder: (context) => TextButton(
            onPressed: () => AppLock.of(context)!.didUnlock(),
            child: const Text('Unlock'),
          ),
        ),
      ),
    );

    final obscurer = find.byKey(appLockContentObscurerKey, skipOffstage: false);

    expect(obscurer, findsNothing);

    await tester.tap(find.text('secret content'));
    await tester.pumpAndSettle();

    expect(obscurer, findsOneWidget);

    await tester.pump(const Duration(seconds: 1));

    expect(obscurer, findsOneWidget);

    await tester.tap(find.text('Unlock'));
    await tester.pumpAndSettle();

    expect(obscurer, findsNothing);
  });

  testWidgets('reports no successful unlock after an unchanged generation', (
    tester,
  ) async {
    await tester.pumpWidget(
      _buildAppLock(ThemeMode.light, child: const Text('Unlocked content')),
    );

    final appLock = AppLock.of(tester.element(find.text('Unlocked content')))!;
    final generation = appLock.successfulUnlockGeneration;

    expect(await appLock.waitForSuccessfulUnlockAfter(generation), isFalse);
  });

  testWidgets('waits for App Lock and reports a successful unlock', (
    tester,
  ) async {
    await tester.pumpWidget(
      _buildAppLock(ThemeMode.light, child: const Text('Unlocked content')),
    );

    final appLock = AppLock.of(tester.element(find.text('Unlocked content')))!;
    final generation = appLock.successfulUnlockGeneration;
    unawaited(appLock.showLockScreen());
    final unlockResult = appLock.waitForSuccessfulUnlockAfter(generation);

    await tester.pumpAndSettle();

    var completed = false;
    unawaited(unlockResult.then((_) => completed = true));
    await tester.pump();
    expect(completed, isFalse);

    appLock.didUnlock();
    await tester.pumpAndSettle();

    expect(await unlockResult, isTrue);
  });

  testWidgets('reports an unlock that completed before the check', (
    tester,
  ) async {
    await tester.pumpWidget(
      _buildAppLock(ThemeMode.light, child: const Text('Unlocked content')),
    );

    final appLock = AppLock.of(tester.element(find.text('Unlocked content')))!;
    final generation = appLock.successfulUnlockGeneration;
    unawaited(appLock.showLockScreen());
    await tester.pumpAndSettle();

    appLock.didUnlock();
    await tester.pumpAndSettle();

    expect(await appLock.waitForSuccessfulUnlockAfter(generation), isTrue);
  });

  testWidgets('reports no unlock when the grace period clears App Lock', (
    tester,
  ) async {
    await tester.pumpWidget(
      _buildAppLock(ThemeMode.light, child: const Text('Unlocked content')),
    );

    final appLock = AppLock.of(tester.element(find.text('Unlocked content')))!;
    appLock.enable();
    final generation = appLock.successfulUnlockGeneration;

    tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.paused);
    await tester.pump();

    final unlockResult = appLock.waitForSuccessfulUnlockAfter(generation);

    tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
    await tester.pumpAndSettle();

    expect(await unlockResult, isFalse);
  });
}

class _TestLockScreenHost implements LockScreenHost {
  @override
  bool isLoggedIn() => true;

  @override
  Future<void> logout() async {}
}

Widget _buildAppLock(
  ThemeMode savedThemeMode, {
  Widget? child,
  Widget lockScreen = const SizedBox.shrink(),
  bool debugShowCheckedModeBanner = true,
}) {
  return AppLock(
    builder: (_) =>
        child ??
        Builder(builder: (context) => Text(Theme.of(context).brightness.name)),
    lockScreen: lockScreen,
    enabled: false,
    savedThemeMode: savedThemeMode,
    lightTheme: ThemeData(brightness: Brightness.light),
    darkTheme: ThemeData(brightness: Brightness.dark),
    debugShowCheckedModeBanner: debugShowCheckedModeBanner,
    supportedLocales: const [Locale('en')],
    localizationsDelegates: const <LocalizationsDelegate<dynamic>>[],
    localeListResolutionCallback: (_, supportedLocales) =>
        supportedLocales.first,
  );
}
