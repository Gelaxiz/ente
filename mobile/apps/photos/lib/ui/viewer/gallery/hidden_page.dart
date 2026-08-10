import "dart:async";

import "package:collection/collection.dart";
import "package:ente_lock_screen/local_authentication_service.dart";
import "package:ente_lock_screen/lock_screen_settings.dart";
import "package:ente_lock_screen/ui/app_lock.dart";
import "package:ente_pure_utils/ente_pure_utils.dart";
import "package:ente_strings/ente_strings.dart";
import "package:flutter/material.dart";
import "package:photos/core/configuration.dart";
import "package:photos/core/event_bus.dart";
import "package:photos/db/files_db.dart";
import "package:photos/events/album_sort_order_change_event.dart";
import "package:photos/events/collection_updated_event.dart";
import "package:photos/events/files_updated_event.dart";
import "package:photos/models/collection/collection.dart";
import "package:photos/models/gallery_type.dart";
import "package:photos/models/selected_files.dart";
import "package:photos/services/collections_service.dart";
import "package:photos/services/hidden_service.dart";
import "package:photos/ui/collections/album/horizontal_list.dart";
import "package:photos/ui/collections/collection_list_page.dart";
import "package:photos/ui/common/loading_widget.dart";
import "package:photos/ui/components/empty_state_component.dart";
import "package:photos/ui/tabs/home_widget.dart";
import "package:photos/ui/viewer/actions/file_selection_overlay_bar.dart";
import "package:photos/ui/viewer/gallery/cleanup_hidden_files_widget.dart";
import "package:photos/ui/viewer/gallery/cleanup_hidden_from_device_widget.dart";
import "package:photos/ui/viewer/gallery/gallery.dart";
import "package:photos/ui/viewer/gallery/gallery_app_bar_widget.dart";
import "package:photos/ui/viewer/gallery/state/gallery_boundaries_provider.dart";
import "package:photos/ui/viewer/gallery/state/gallery_files_inherited_widget.dart";
import "package:photos/ui/viewer/gallery/state/selection_state.dart";

Future<void> _enableScreenCoverForHidden() async {
  if (!LockScreenSettings.instance.getShouldHideAppContent()) {
    await LockScreenSettings.instance.setHideAppContent(true, persist: false);
  }
}

Future<void> _restoreScreenCoverPreference() async {
  if (!LockScreenSettings.instance.getShouldHideAppContent()) {
    await LockScreenSettings.instance.setHideAppContent(false, persist: false);
  }
}

void _returnToHome(BuildContext context) {
  Navigator.of(context)
      .pushAndRemoveUntil<void>(
        PageRouteBuilder<void>(
          opaque: true,
          pageBuilder: (_, _, _) => const HomeWidget(),
          transitionDuration: Duration.zero,
          reverseTransitionDuration: Duration.zero,
        ),
        (_) => false,
      )
      .ignore();
  WidgetsBinding.instance.addPostFrameCallback((_) {
    unawaited(_restoreScreenCoverPreference());
  });
}

class HiddenPage extends StatefulWidget {
  final String tagPrefix;
  final GalleryType appBarType;
  final GalleryType overlayType;

  const HiddenPage({
    this.tagPrefix = "hidden_page",
    this.appBarType = GalleryType.hiddenSection,
    this.overlayType = GalleryType.hiddenSection,
    super.key,
  });

  @override
  State<HiddenPage> createState() => _HiddenPageState();
}

class _HiddenReauthenticationGate extends StatefulWidget {
  const _HiddenReauthenticationGate();

  @override
  State<_HiddenReauthenticationGate> createState() =>
      _HiddenReauthenticationGateState();
}

class _HiddenReauthenticationGateState
    extends State<_HiddenReauthenticationGate>
    with WidgetsBindingObserver {
  bool _authenticationStarted = false;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    WidgetsBinding.instance.addPostFrameCallback((_) {
      _authenticateIfResumed();
    });
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) {
      WidgetsBinding.instance.addPostFrameCallback((_) {
        _authenticateIfResumed();
      });
    }
  }

  void _authenticateIfResumed() {
    if (!mounted ||
        _authenticationStarted ||
        WidgetsBinding.instance.lifecycleState != AppLifecycleState.resumed) {
      return;
    }
    _authenticationStarted = true;
    unawaited(_authenticate());
  }

  Future<void> _authenticate() async {
    await AppLock.of(context)?.waitUntilUnlocked();
    if (!mounted) {
      return;
    }
    final authenticated = await LocalAuthenticationService.instance
        .requestLocalAuthentication(
          context,
          context.strings.authToViewYourHiddenFiles,
          useDebugAuthCache: false,
        );
    if (!mounted) {
      return;
    }
    if (authenticated) {
      Navigator.of(context).pop(true);
    } else {
      _goHome();
    }
  }

  void _goHome() {
    _returnToHome(context);
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return PopScope(
      canPop: false,
      onPopInvokedWithResult: (didPop, _) {
        if (!didPop) {
          _goHome();
        }
      },
      child: Scaffold(
        backgroundColor: Theme.of(context).scaffoldBackgroundColor,
      ),
    );
  }
}

class _HiddenPageState extends State<HiddenPage> with WidgetsBindingObserver {
  int? _defaultHiddenCollectionId;
  final _hiddenCollectionsExcludingDefault = <Collection>[];
  bool _hasFilesNeedingCleanup = false;
  bool _hasHiddenFilesOnDevice = false;
  bool _isReauthenticationPending = false;
  late StreamSubscription<CollectionUpdatedEvent>
  _collectionUpdatesSubscription;
  late StreamSubscription<AlbumSortOrderChangeEvent> _albumSortOrderChangeEvent;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    unawaited(_enableScreenCoverForHidden());
    _collectionUpdatesSubscription = Bus.instance
        .on<CollectionUpdatedEvent>()
        .listen((event) {
          unawaited(_refreshHiddenCollections());
          _checkForCleanupNeeded();
          _checkForDeviceCleanupNeeded();
        });
    _albumSortOrderChangeEvent = Bus.instance
        .on<AlbumSortOrderChangeEvent>()
        .listen((event) {
          unawaited(_refreshHiddenCollections());
        });
    unawaited(_refreshHiddenCollections());
    _checkForCleanupNeeded();
    _checkForDeviceCleanupNeeded();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    final hasLeftForeground =
        state == AppLifecycleState.hidden || state == AppLifecycleState.paused;
    if (!hasLeftForeground || !mounted || _isReauthenticationPending) {
      return;
    }
    _isReauthenticationPending = true;
    unawaited(_showReauthenticationGate());
  }

  Future<void> _showReauthenticationGate() async {
    final authenticated = await Navigator.of(context).push<bool>(
      PageRouteBuilder<bool>(
        opaque: true,
        pageBuilder: (_, _, _) => const _HiddenReauthenticationGate(),
        transitionDuration: Duration.zero,
        reverseTransitionDuration: Duration.zero,
      ),
    );
    if (!mounted || authenticated != true) {
      return;
    }
    _isReauthenticationPending = false;
  }

  Future<void> _checkForCleanupNeeded() async {
    final hasCleanup = await CollectionsService.instance
        .hasFilesNeedingHiddenCleanup();
    if (mounted && hasCleanup != _hasFilesNeedingCleanup) {
      setState(() {
        _hasFilesNeedingCleanup = hasCleanup;
      });
    }
  }

  Future<void> _checkForDeviceCleanupNeeded() async {
    final hasDeviceFiles = await CollectionsService.instance
        .hasHiddenFilesOnDevice();
    if (mounted && hasDeviceFiles != _hasHiddenFilesOnDevice) {
      setState(() {
        _hasHiddenFilesOnDevice = hasDeviceFiles;
      });
    }
  }

  Future<void> _refreshHiddenCollections() async {
    final hiddenCollections = CollectionsService.instance
        .getHiddenCollections();
    final defaultHiddenCollection = await CollectionsService.instance
        .getDefaultHiddenCollection();
    final hiddenCollectionsExcludingDefault = hiddenCollections
        .where((c) => c.id != defaultHiddenCollection.id)
        .toList();
    await CollectionsService.instance.sortCollectionsByAlbumPreferences(
      hiddenCollectionsExcludingDefault,
    );
    if (!mounted) {
      return;
    }
    setState(() {
      _hiddenCollectionsExcludingDefault
        ..clear()
        ..addAll(hiddenCollectionsExcludingDefault);
      _defaultHiddenCollectionId = defaultHiddenCollection.id;
    });
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    if (!_isReauthenticationPending) {
      unawaited(_restoreScreenCoverPreference());
    }
    _collectionUpdatesSubscription.cancel();
    _albumSortOrderChangeEvent.cancel();
    super.dispose();
  }

  final _selectedFiles = SelectedFiles();

  @override
  Widget build(BuildContext context) {
    if (_defaultHiddenCollectionId == null) {
      return const EnteLoadingWidget();
    }
    final appBar = GalleryAppBarWidget.sliverConfig(
      widget.appBarType,
      context.strings.hidden,
      _selectedFiles,
    );
    final gallery = Gallery(
      appBar: appBar,
      asyncLoader: (creationStartTime, creationEndTime, {limit, asc}) {
        return FilesDB.instance.getFilesInCollections(
          [_defaultHiddenCollectionId!],
          creationStartTime,
          creationEndTime,
          Configuration.instance.getUserID()!,
          limit: limit,
          asc: asc,
        );
      },
      reloadEvent: Bus.instance.on<FilesUpdatedEvent>().where(
        (event) =>
            event.updatedFiles.firstWhereOrNull(
              (element) => element.uploadedFileID != null,
            ) !=
            null,
      ),
      removalEventTypes: const {
        EventType.unhide,
        EventType.deletedFromEverywhere,
        EventType.deletedFromRemote,
      },
      forceReloadEvents: [
        Bus.instance.on<FilesUpdatedEvent>().where(
          (event) =>
              event.updatedFiles.firstWhereOrNull(
                (element) => element.uploadedFileID != null,
              ) !=
              null,
        ),
      ],
      tagPrefix: widget.tagPrefix,
      selectedFiles: _selectedFiles,
      initialFiles: null,
      emptyState: _hiddenCollectionsExcludingDefault.isEmpty
          ? EmptyStateComponent(
              assetPath: "assets/empty_state_hidden.png",
              title: context.strings.hiddenItemsWillShowUpHere,
            )
          : const SizedBox.shrink(),
      header: Column(
        children: [
          RepaintBoundary(
            child: AnimatedCrossFade(
              firstCurve: Curves.easeInOutQuart,
              secondCurve: Curves.easeInOutQuart,
              sizeCurve: Curves.easeInOutQuart,
              firstChild: CleanupHiddenFilesWidget(
                onCleanupComplete: () => _checkForCleanupNeeded(),
              ),
              secondChild: const SizedBox(width: double.infinity),
              crossFadeState: _hasFilesNeedingCleanup
                  ? CrossFadeState.showFirst
                  : CrossFadeState.showSecond,
              duration: const Duration(milliseconds: 750),
            ),
          ),
          RepaintBoundary(
            child: AnimatedCrossFade(
              firstCurve: Curves.easeInOutQuart,
              secondCurve: Curves.easeInOutQuart,
              sizeCurve: Curves.easeInOutQuart,
              firstChild: CleanupHiddenFromDeviceWidget(
                onCleanupComplete: () => _checkForDeviceCleanupNeeded(),
              ),
              secondChild: const SizedBox(width: double.infinity),
              crossFadeState: _hasHiddenFilesOnDevice
                  ? CrossFadeState.showFirst
                  : CrossFadeState.showSecond,
              duration: const Duration(milliseconds: 750),
            ),
          ),
          AlbumHorizontalList(
            () async {
              return _hiddenCollectionsExcludingDefault;
            },
            hasVerifiedLock: true,
            onViewAllTapped: () async {
              await routeToPage(
                context,
                CollectionListPage(
                  _hiddenCollectionsExcludingDefault,
                  sectionType: UISectionType.hiddenCollections,
                  appTitle: Text(context.strings.hidden),
                  tag: "hidden",
                ),
              );
            },
          ),
        ],
      ),
    );
    return GalleryBoundariesProvider(
      child: GalleryFilesState(
        child: Scaffold(
          body: SelectionState(
            selectedFiles: _selectedFiles,
            child: Stack(
              alignment: Alignment.bottomCenter,
              children: [
                gallery,
                FileSelectionOverlayBar(widget.overlayType, _selectedFiles),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
