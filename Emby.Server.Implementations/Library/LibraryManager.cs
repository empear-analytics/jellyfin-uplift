#pragma warning disable CS1591
#pragma warning disable CA5394

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BitFaster.Caching.Lru;
using Emby.Naming.Common;
using Emby.Naming.TV;
using Emby.Naming.Video;
using Emby.Server.Implementations.Library.Resolvers;
using Emby.Server.Implementations.Playlists;
using Emby.Server.Implementations.ScheduledTasks.Tasks;
using Emby.Server.Implementations.Sorting;
using Jellyfin.Data;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Extensions;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.IO;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Resolvers;
using MediaBrowser.Controller.Sorting;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Library;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;
using Episode = MediaBrowser.Controller.Entities.TV.Episode;
using EpisodeInfo = Emby.Naming.TV.EpisodeInfo;
using Genre = MediaBrowser.Controller.Entities.Genre;
using Person = MediaBrowser.Controller.Entities.Person;
using VideoResolver = Emby.Naming.Video.VideoResolver;

namespace Emby.Server.Implementations.Library
{
    /// <summary>
    /// Class LibraryManager.
    /// </summary>
    public partial class LibraryManager : ILibraryManager
    {
        private const string ShortcutFileExtension = ".mblink";

        private readonly ILogger<LibraryManager> _logger;
        private readonly ITaskManager _taskManager;
        private readonly IUserManager _userManager;
        private readonly IUserDataManager _userDataManager;
        private readonly IServerConfigurationManager _configurationManager;
        private readonly Lazy<ILibraryMonitor> _libraryMonitorFactory;
        private readonly Lazy<IProviderManager> _providerManagerFactory;
        private readonly Lazy<IUserViewManager> _userViewManagerFactory;
        private readonly IServerApplicationHost _appHost;
        private readonly IFileSystem _fileSystem;
        private readonly IItemRepository _itemRepository;
        private readonly IItemPersistenceService _persistenceService;
        private readonly INextUpService _nextUpService;
        private readonly IItemCountService _countService;
        private readonly ILinkedChildrenService _linkedChildrenService;
        private readonly IImageProcessor _imageProcessor;
        private readonly NamingOptions _namingOptions;
        private readonly IPeopleRepository _peopleRepository;
        private readonly ExtraResolver _extraResolver;
        private readonly IPathManager _pathManager;
        private readonly ILocalizationManager _localization;
        private readonly IDirectoryService _directoryService;
        private readonly FastConcurrentLru<Guid, BaseItem> _cache;
        private readonly DotIgnoreIgnoreRule _dotIgnoreIgnoreRule;
        private readonly IMediaStreamRepository _mediaStreamRepository;
        private readonly Lazy<IExternalDataManager> _externalDataManagerFactory;

        /// <summary>
        /// The _root folder sync lock.
        /// </summary>
        private readonly Lock _rootFolderSyncLock = new();
        private readonly Lock _userRootFolderSyncLock = new();

        private readonly TimeSpan _viewRefreshInterval = TimeSpan.FromHours(24);

        /// <summary>
        /// The _root folder.
        /// </summary>
        private volatile AggregateFolder? _rootFolder;
        private volatile UserRootFolder? _userRootFolder;

        private bool _wizardCompleted;

        /// <summary>
        /// Initializes a new instance of the <see cref="LibraryManager" /> class.
        /// </summary>
        /// <param name="appHost">The application host.</param>
        /// <param name="loggerFactory">The logger factory.</param>
        /// <param name="taskManager">The task manager.</param>
        /// <param name="userManager">The user manager.</param>
        /// <param name="configurationManager">The configuration manager.</param>
        /// <param name="userDataManager">The user data manager.</param>
        /// <param name="libraryMonitorFactory">The library monitor.</param>
        /// <param name="fileSystem">The file system.</param>
        /// <param name="providerManagerFactory">The provider manager.</param>
        /// <param name="userViewManagerFactory">The user view manager.</param>
        /// <param name="itemRepository">The item repository.</param>
        /// <param name="persistenceService">The item persistence service.</param>
        /// <param name="nextUpService">The next up service.</param>
        /// <param name="countService">The item count service.</param>
        /// <param name="linkedChildrenService">The linked children service.</param>
        /// <param name="imageProcessor">The image processor.</param>
        /// <param name="namingOptions">The naming options.</param>
        /// <param name="directoryService">The directory service.</param>
        /// <param name="peopleRepository">The people repository.</param>
        /// <param name="pathManager">The path manager.</param>
        /// <param name="dotIgnoreIgnoreRule">The .ignore rule handler.</param>
        /// <param name="localization">The localization manager.</param>
        /// <param name="mediaStreamRepository">The media stream repository.</param>
        /// <param name="externalDataManagerFactory">The external data manager (lazy, to break the DI cycle through ChapterManager).</param>
        public LibraryManager(
            IServerApplicationHost appHost,
            ILoggerFactory loggerFactory,
            ITaskManager taskManager,
            IUserManager userManager,
            IServerConfigurationManager configurationManager,
            IUserDataManager userDataManager,
            Lazy<ILibraryMonitor> libraryMonitorFactory,
            IFileSystem fileSystem,
            Lazy<IProviderManager> providerManagerFactory,
            Lazy<IUserViewManager> userViewManagerFactory,
            IItemRepository itemRepository,
            IItemPersistenceService persistenceService,
            INextUpService nextUpService,
            IItemCountService countService,
            ILinkedChildrenService linkedChildrenService,
            IImageProcessor imageProcessor,
            NamingOptions namingOptions,
            IDirectoryService directoryService,
            IPeopleRepository peopleRepository,
            IPathManager pathManager,
            DotIgnoreIgnoreRule dotIgnoreIgnoreRule,
            ILocalizationManager localization,
            IMediaStreamRepository mediaStreamRepository,
            Lazy<IExternalDataManager> externalDataManagerFactory)
        {
            _appHost = appHost;
            _logger = loggerFactory.CreateLogger<LibraryManager>();
            _taskManager = taskManager;
            _userManager = userManager;
            _configurationManager = configurationManager;
            _userDataManager = userDataManager;
            _libraryMonitorFactory = libraryMonitorFactory;
            _fileSystem = fileSystem;
            _providerManagerFactory = providerManagerFactory;
            _userViewManagerFactory = userViewManagerFactory;
            _itemRepository = itemRepository;
            _persistenceService = persistenceService;
            _nextUpService = nextUpService;
            _countService = countService;
            _linkedChildrenService = linkedChildrenService;
            _imageProcessor = imageProcessor;

            _cache = new FastConcurrentLru<Guid, BaseItem>(_configurationManager.Configuration.CacheSize);

            _namingOptions = namingOptions;
            _peopleRepository = peopleRepository;
            _pathManager = pathManager;
            _dotIgnoreIgnoreRule = dotIgnoreIgnoreRule;
            _localization = localization;
            _directoryService = directoryService;
            _extraResolver = new ExtraResolver(loggerFactory.CreateLogger<ExtraResolver>(), namingOptions, directoryService);

            _configurationManager.ConfigurationUpdated += ConfigurationUpdated;

            _mediaStreamRepository = mediaStreamRepository;
            _externalDataManagerFactory = externalDataManagerFactory;

            RecordConfigurationValues(_configurationManager.Configuration);
        }

        /// <summary>
        /// Occurs when [item added].
        /// </summary>
        public event EventHandler<ItemChangeEventArgs>? ItemAdded;

        /// <summary>
        /// Occurs when [item updated].
        /// </summary>
        public event EventHandler<ItemChangeEventArgs>? ItemUpdated;

        /// <summary>
        /// Occurs when [item removed].
        /// </summary>
        public event EventHandler<ItemChangeEventArgs>? ItemRemoved;

        /// <summary>
        /// Gets the root folder.
        /// </summary>
        /// <value>The root folder.</value>
        public AggregateFolder RootFolder
        {
            get
            {
                if (_rootFolder is null)
                {
                    lock (_rootFolderSyncLock)
                    {
                        _rootFolder ??= CreateRootFolder();
                    }
                }

                return _rootFolder;
            }
        }

        private ILibraryMonitor LibraryMonitor => _libraryMonitorFactory.Value;

        private IProviderManager ProviderManager => _providerManagerFactory.Value;

        private IUserViewManager UserViewManager => _userViewManagerFactory.Value;

        /// <summary>
        /// Gets or sets the postscan tasks.
        /// </summary>
        /// <value>The postscan tasks.</value>
        private ILibraryPostScanTask[] PostScanTasks { get; set; } = [];

        /// <summary>
        /// Gets or sets the intro providers.
        /// </summary>
        /// <value>The intro providers.</value>
        private IIntroProvider[] IntroProviders { get; set; } = [];

        /// <summary>
        /// Gets or sets the list of entity resolution ignore rules.
        /// </summary>
        /// <value>The entity resolution ignore rules.</value>
        private IResolverIgnoreRule[] EntityResolutionIgnoreRules { get; set; } = [];

        /// <summary>
        /// Gets or sets the list of currently registered entity resolvers.
        /// </summary>
        /// <value>The entity resolvers enumerable.</value>
        private IItemResolver[] EntityResolvers { get; set; } = [];

        private IMultiItemResolver[] MultiItemResolvers { get; set; } = [];

        /// <summary>
        /// Gets or sets the comparers.
        /// </summary>
        /// <value>The comparers.</value>
        private IBaseItemComparer[] Comparers { get; set; } = [];

        public bool IsScanRunning { get; private set; }

        /// <summary>
        /// Adds the parts.
        /// </summary>
        /// <param name="rules">The rules.</param>
        /// <param name="resolvers">The resolvers.</param>
        /// <param name="introProviders">The intro providers.</param>
        /// <param name="itemComparers">The item comparers.</param>
        /// <param name="postScanTasks">The post scan tasks.</param>
        public void AddParts(
            IEnumerable<IResolverIgnoreRule> rules,
            IEnumerable<IItemResolver> resolvers,
            IEnumerable<IIntroProvider> introProviders,
            IEnumerable<IBaseItemComparer> itemComparers,
            IEnumerable<ILibraryPostScanTask> postScanTasks)
        {
            EntityResolutionIgnoreRules = rules.ToArray();
            EntityResolvers = resolvers.OrderBy(i => i.Priority).ToArray();
            MultiItemResolvers = EntityResolvers.OfType<IMultiItemResolver>().ToArray();
            IntroProviders = introProviders.ToArray();
            Comparers = itemComparers.ToArray();
            PostScanTasks = postScanTasks.ToArray();
        }

        /// <summary>
        /// Records the configuration values.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        private void RecordConfigurationValues(ServerConfiguration configuration)
        {
            _wizardCompleted = configuration.IsStartupWizardCompleted;
        }

        /// <summary>
        /// Configurations the updated.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs" /> instance containing the event data.</param>
        private void ConfigurationUpdated(object? sender, EventArgs e)
        {
            var config = _configurationManager.Configuration;

            var wizardChanged = config.IsStartupWizardCompleted != _wizardCompleted;

            RecordConfigurationValues(config);

            if (wizardChanged)
            {
                QueueLibraryScan();
            }
        }

        public void RegisterItem(BaseItem item)
        {
            ArgumentNullException.ThrowIfNull(item);

            if (item is IItemByName)
            {
                if (item is not MusicArtist)
                {
                    return;
                }
            }
            else if (!item.IsFolder)
            {
                if (item is not Video && item is not LiveTvChannel)
                {
                    return;
                }
            }

            _cache.AddOrUpdate(item.Id, item);
        }

        /// <summary>
        /// Creates the root media folder.
        /// </summary>
        /// <returns>AggregateFolder.</returns>
        /// <exception cref="InvalidOperationException">Cannot create the root folder until plugins have loaded.</exception>
        public AggregateFolder CreateRootFolder()
        {
            var rootFolderPath = _configurationManager.ApplicationPaths.RootFolderPath;

            var rootFolder = GetItemById(GetNewItemId(rootFolderPath, typeof(AggregateFolder))) as AggregateFolder ??
                             (ResolvePath(_fileSystem.GetDirectoryInfo(rootFolderPath)) as Folder ?? throw new InvalidOperationException("Something went very wong"))
                             .DeepCopy<Folder, AggregateFolder>();

            // In case program data folder was moved
            if (!string.Equals(rootFolder.Path, rootFolderPath, StringComparison.Ordinal))
            {
                _logger.LogInformation("Resetting root folder path to {0}", rootFolderPath);
                rootFolder.Path = rootFolderPath;
            }

            // Add in the plug-in folders
            var path = Path.Combine(_configurationManager.ApplicationPaths.DataPath, "playlists");

            var info = Directory.CreateDirectory(path);
            Folder folder = new PlaylistsFolder
            {
                Path = path,
                DateCreated = info.CreationTimeUtc,
                DateModified = info.LastWriteTimeUtc,
            };

            if (folder.Id.IsEmpty())
            {
                folder.Id = GetNewItemId(folder.Path, folder.GetType());
            }

            var dbItem = GetItemById(folder.Id) as BasePluginFolder;

            if (dbItem is not null && string.Equals(dbItem.Path, folder.Path, StringComparison.OrdinalIgnoreCase))
            {
                folder = dbItem;
            }

            if (!folder.ParentId.Equals(rootFolder.Id))
            {
                rootFolder.UpdateToRepositoryAsync(ItemUpdateType.MetadataImport, CancellationToken.None).GetAwaiter().GetResult();
                folder.ParentId = rootFolder.Id;
                folder.UpdateToRepositoryAsync(ItemUpdateType.MetadataImport, CancellationToken.None).GetAwaiter().GetResult();
            }

            rootFolder.AddVirtualChild(folder);

            RegisterItem(folder);

            return rootFolder;
        }

        public Folder GetUserRootFolder()
        {
            if (_userRootFolder is null)
            {
                lock (_userRootFolderSyncLock)
                {
                    if (_userRootFolder is null)
                    {
                        var userRootPath = _configurationManager.ApplicationPaths.DefaultUserViewsPath;

                        _logger.LogDebug("Creating userRootPath at {Path}", userRootPath);
                        Directory.CreateDirectory(userRootPath);

                        var newItemId = GetNewItemId(userRootPath, typeof(UserRootFolder));
                        UserRootFolder? tmpItem = null;
                        try
                        {
                            tmpItem = GetItemById(newItemId) as UserRootFolder;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error creating UserRootFolder {Path}", newItemId);
                        }

                        if (tmpItem is null)
                        {
                            _logger.LogDebug("Creating new userRootFolder with DeepCopy");
                            tmpItem = (ResolvePath(_fileSystem.GetDirectoryInfo(userRootPath)) as Folder ?? throw new InvalidOperationException("Failed to get user root path"))
                                        .DeepCopy<Folder, UserRootFolder>();
                        }

                        // In case program data folder was moved
                        if (!string.Equals(tmpItem.Path, userRootPath, StringComparison.Ordinal))
                        {
                            _logger.LogInformation("Resetting user root folder path to {0}", userRootPath);
                            tmpItem.Path = userRootPath;
                        }

                        _userRootFolder = tmpItem;
                        _logger.LogDebug("Setting userRootFolder: {Folder}", _userRootFolder);
                    }
                }
            }

            return _userRootFolder;
        }

        /// <inheritdoc />
        public BaseItem? FindByPath(string path, bool? isFolder)
        {
            // If this returns multiple items it could be tricky figuring out which one is correct.
            // In most cases, the newest one will be and the others obsolete but not yet cleaned up
            ArgumentException.ThrowIfNullOrEmpty(path);

            var query = new InternalItemsQuery
            {
                Path = path,
                IsFolder = isFolder,
                OrderBy = [(ItemSortBy.DateCreated, SortOrder.Descending)],
                Limit = 1,
                DtoOptions = new DtoOptions(true)
            };

            return GetItemList(query)
                .FirstOrDefault();
        }

        /// <summary>
        /// Reloads the root media folder.
        /// </summary>
        /// <param name="progress">The progress.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task.</returns>
        public Task ValidateMediaLibrary(IProgress<double> progress, CancellationToken cancellationToken)
        {
            // Just run the scheduled task so that the user can see it
            _taskManager.CancelIfRunningAndQueue<RefreshMediaLibraryTask>();

            return Task.CompletedTask;
        }

        /// <summary>
        /// Validates the media library internal.
        /// </summary>
        /// <param name="progress">The progress.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task ValidateMediaLibraryInternal(IProgress<double> progress, CancellationToken cancellationToken)
        {
            IsScanRunning = true;
            ClearIgnoreRuleCache();
            LibraryMonitor.Stop();

            try
            {
                await PerformLibraryValidation(progress, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                ClearIgnoreRuleCache();
                LibraryMonitor.Start();
                IsScanRunning = false;
            }
        }

        public async Task ValidateTopLibraryFolders(CancellationToken cancellationToken, bool removeRoot = false)
        {
            ClearIgnoreRuleCache();
            RootFolder.Children = null;
            await RootFolder.RefreshMetadata(cancellationToken).ConfigureAwait(false);

            // Start by just validating the children of the root, but go no further
            await RootFolder.ValidateChildren(
                new Progress<double>(),
                new MetadataRefreshOptions(new DirectoryService(_fileSystem)),
                recursive: false,
                allowRemoveRoot: removeRoot,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var rootFolder = GetUserRootFolder();
            rootFolder.Children = null;

            await rootFolder.RefreshMetadata(cancellationToken).ConfigureAwait(false);

            await rootFolder.ValidateChildren(
                new Progress<double>(),
                new MetadataRefreshOptions(new DirectoryService(_fileSystem)),
                recursive: false,
                allowRemoveRoot: removeRoot,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            // Quickly scan CollectionFolders for changes
            var toDelete = new List<Guid>();
            foreach (var child in rootFolder.Children!.OfType<Folder>())
            {
                // If the user has somehow deleted the collection directory, remove the metadata from the database.
                if (child is CollectionFolder collectionFolder && !Directory.Exists(collectionFolder.Path))
                {
                    toDelete.Add(collectionFolder.Id);
                }
                else
                {
                    await child.RefreshMetadata(cancellationToken).ConfigureAwait(false);
                }
            }

            if (toDelete.Count > 0)
            {
                _persistenceService.DeleteItem(toDelete.ToArray());
            }

            ClearIgnoreRuleCache();
        }

        /// <inheritdoc />
        public void ClearIgnoreRuleCache()
        {
            _dotIgnoreIgnoreRule.ClearDirectoryCache();
        }

        private async Task PerformLibraryValidation(IProgress<double> progress, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Validating media library");

            await ValidateTopLibraryFolders(cancellationToken).ConfigureAwait(false);

            var innerProgress = new Progress<double>(pct => progress.Report(pct * 0.96));

            // Validate the entire media library
            await RootFolder.ValidateChildren(innerProgress, new MetadataRefreshOptions(new DirectoryService(_fileSystem)), recursive: true, cancellationToken: cancellationToken).ConfigureAwait(false);

            progress.Report(96);

            innerProgress = new Progress<double>(pct => progress.Report(96 + (pct * .04)));

            await RunPostScanTasks(innerProgress, cancellationToken).ConfigureAwait(false);

            progress.Report(100);
        }

        /// <summary>
        /// Runs the post scan tasks.
        /// </summary>
        /// <param name="progress">The progress.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task.</returns>
        private async Task RunPostScanTasks(IProgress<double> progress, CancellationToken cancellationToken)
        {
            var tasks = PostScanTasks.ToList();

            var numComplete = 0;
            var numTasks = tasks.Count;

            _logger.LogInformation("Running {TaskCount} post-scan task(s)", numTasks);

            var phaseStart = Stopwatch.GetTimestamp();

            foreach (var task in tasks)
            {
                // Prevent access to modified closure
                var currentNumComplete = numComplete;

                var innerProgress = new Progress<double>(pct =>
                {
                    double innerPercent = pct;
                    innerPercent /= 100;
                    innerPercent += currentNumComplete;

                    innerPercent /= numTasks;
                    innerPercent *= 100;

                    progress.Report(innerPercent);
                });

                var taskName = task.GetType().Name;
                var taskStart = Stopwatch.GetTimestamp();

                _logger.LogInformation(
                    "Running post-scan task {TaskNumber}/{TaskCount}: {TaskName}",
                    currentNumComplete + 1,
                    numTasks,
                    taskName);

                try
                {
                    await task.Run(innerProgress, cancellationToken).ConfigureAwait(false);

                    var elapsed = Stopwatch.GetElapsedTime(taskStart);
                    _logger.LogInformation(
                        "Post-scan task {TaskName} completed after {Minutes} minute(s) and {Seconds} seconds",
                        taskName,
                        Math.Truncate(elapsed.TotalMinutes),
                        elapsed.Seconds);
                }
                catch (OperationCanceledException)
                {
                    var elapsed = Stopwatch.GetElapsedTime(taskStart);
                    _logger.LogInformation(
                        "Post-scan task {TaskName} cancelled after {Minutes} minute(s) and {Seconds} seconds",
                        taskName,
                        Math.Truncate(elapsed.TotalMinutes),
                        elapsed.Seconds);
                    throw;
                }
                catch (Exception ex)
                {
                    var elapsed = Stopwatch.GetElapsedTime(taskStart);
                    _logger.LogError(
                        ex,
                        "Post-scan task {TaskName} failed after {Minutes} minute(s) and {Seconds} seconds",
                        taskName,
                        Math.Truncate(elapsed.TotalMinutes),
                        elapsed.Seconds);
                }

                numComplete++;
                double percent = numComplete;
                percent /= numTasks;
                progress.Report(percent * 100);
            }

            var phaseElapsed = Stopwatch.GetElapsedTime(phaseStart);
            _logger.LogInformation(
                "All post-scan tasks completed after {Minutes} minute(s) and {Seconds} seconds",
                Math.Truncate(phaseElapsed.TotalMinutes),
                phaseElapsed.Seconds);

            _persistenceService.UpdateInheritedValues();

            progress.Report(100);
        }

        /// <inheritdoc />
        public BaseItem? GetItemById(Guid id)
        {
            if (id.IsEmpty())
            {
                throw new ArgumentException("Guid can't be empty", nameof(id));
            }

            if (_cache.TryGet(id, out var item))
            {
                return item;
            }

            item = RetrieveItem(id);

            if (item is not null)
            {
                RegisterItem(item);
            }

            return item;
        }

        /// <inheritdoc />
        public T? GetItemById<T>(Guid id)
            where T : BaseItem
        {
            var item = GetItemById(id);
            if (item is T typedItem)
            {
                return typedItem;
            }

            return null;
        }

        /// <inheritdoc />
        public T? GetItemById<T>(Guid id, Guid userId)
            where T : BaseItem
        {
            var user = userId.IsEmpty() ? null : _userManager.GetUserById(userId);
            return GetItemById<T>(id, user);
        }

        /// <inheritdoc />
        public T? GetItemById<T>(Guid id, User? user)
            where T : BaseItem
        {
            var item = GetItemById<T>(id);
            return ItemIsVisible(item, user) ? item : null;
        }

        public IReadOnlyList<BaseItem> GetItemList(InternalItemsQuery query, bool allowExternalContent)
        {
            SetTopParentIdsFromParentId(query);

            if (query.User is not null)
            {
                AddUserToQuery(query, query.User, allowExternalContent);
            }

            return _itemRepository.GetItemList(query);
        }

        public IReadOnlyList<BaseItem> GetItemList(InternalItemsQuery query)
        {
            return GetItemList(query, true);
        }

        public int GetCount(InternalItemsQuery query)
        {
            SetTopParentIdsFromParentId(query);

            if (query.User is not null)
            {
                AddUserToQuery(query, query.User);
            }

            return _countService.GetCount(query);
        }

        public ItemCounts GetItemCounts(InternalItemsQuery query)
        {
            SetTopParentIdsFromParentId(query);

            if (query.User is not null)
            {
                AddUserToQuery(query, query.User);
            }

            return _countService.GetItemCounts(query);
        }

        /// <inheritdoc/>
        public ItemCounts GetItemCountsForNameItem(BaseItemKind kind, Guid id, BaseItemKind[] relatedItemKinds, User? user)
        {
            var query = new InternalItemsQuery(user);
            if (user is not null)
            {
                AddUserToQuery(query, user);
            }

            return _countService.GetItemCountsForNameItem(kind, id, relatedItemKinds, query);
        }

        /// <inheritdoc/>
        public Dictionary<Guid, ItemCounts> GetItemCountsForNameItems(BaseItemKind kind, IReadOnlyList<Guid> ids, BaseItemKind[] relatedItemKinds, User? user)
        {
            var query = new InternalItemsQuery(user);
            if (user is not null)
            {
                AddUserToQuery(query, user);
            }

            return _countService.GetItemCountsForNameItems(kind, ids, relatedItemKinds, query);
        }

        public Dictionary<Guid, int> GetChildCountBatch(IReadOnlyList<Guid> parentIds, User? user)
        {
            return _countService.GetChildCountBatch(parentIds, user);
        }

        /// <inheritdoc/>
        public Dictionary<Guid, (int Played, int Total)> GetPlayedAndTotalCountBatch(IReadOnlyList<Guid> folderIds, User user)
        {
            return _countService.GetPlayedAndTotalCountBatch(folderIds, user);
        }

        public IReadOnlyList<BaseItem> GetItemList(InternalItemsQuery query, List<BaseItem> parents)
        {
            ScopeQueryToParents(query, parents);

            return _itemRepository.GetItemList(query);
        }

        public IReadOnlyList<BaseItem> GetLatestItemList(InternalItemsQuery query, IReadOnlyList<BaseItem> parents, CollectionType collectionType)
        {
            ScopeQueryToParents(query, parents);

            return _itemRepository.GetLatestItemList(query, collectionType);
        }

        public IReadOnlyList<string> GetNextUpSeriesKeys(InternalItemsQuery query, IReadOnlyCollection<BaseItem> parents, DateTime dateCutoff)
        {
            ScopeQueryToParents(query, parents);

            return _nextUpService.GetNextUpSeriesKeys(query, dateCutoff);
        }

        /// <inheritdoc />
        public IReadOnlyDictionary<string, MediaBrowser.Controller.Persistence.NextUpEpisodeBatchResult> GetNextUpEpisodesBatch(
            InternalItemsQuery query,
            IReadOnlyList<string> seriesKeys,
            bool includeSpecials,
            bool includeWatchedForRewatching)
        {
            return _nextUpService.GetNextUpEpisodesBatch(query, seriesKeys, includeSpecials, includeWatchedForRewatching);
        }

        public QueryResult<BaseItem> QueryItems(InternalItemsQuery query)
        {
            return QueryItemsForUser(query);
        }

        public IReadOnlyList<Guid> GetItemIds(InternalItemsQuery query)
        {
            if (query.User is not null)
            {
                AddUserToQuery(query, query.User);
            }

            return _itemRepository.GetItemIdsList(query);
        }

        public QueryResult<(BaseItem Item, ItemCounts ItemCounts)> GetStudios(InternalItemsQuery query)
        {
            if (query.User is not null)
            {
                AddUserToQuery(query, query.User);
            }

            SetTopParentOrAncestorIds(query);
            return _itemRepository.GetStudios(query);
        }

        public QueryResult<(BaseItem Item, ItemCounts ItemCounts)> GetGenres(InternalItemsQuery query)
        {
            if (query.User is not null)
            {
                AddUserToQuery(query, query.User);
            }

            SetTopParentOrAncestorIds(query);
            return _itemRepository.GetGenres(query);
        }

        public QueryResult<(BaseItem Item, ItemCounts ItemCounts)> GetMusicGenres(InternalItemsQuery query)
        {
            if (query.User is not null)
            {
                AddUserToQuery(query, query.User);
            }

            SetTopParentOrAncestorIds(query);
            return _itemRepository.GetMusicGenres(query);
        }

        public QueryResult<(BaseItem Item, ItemCounts ItemCounts)> GetAllArtists(InternalItemsQuery query)
        {
            if (query.User is not null)
            {
                AddUserToQuery(query, query.User);
            }

            SetTopParentOrAncestorIds(query);
            return _itemRepository.GetAllArtists(query);
        }

        public QueryResult<(BaseItem Item, ItemCounts ItemCounts)> GetArtists(InternalItemsQuery query)
        {
            if (query.User is not null)
            {
                AddUserToQuery(query, query.User);
            }

            SetTopParentOrAncestorIds(query);
            return _itemRepository.GetArtists(query);
        }

        private void SetTopParentOrAncestorIds(InternalItemsQuery query)
        {
            var ancestorIds = query.AncestorIds;
            int len = ancestorIds.Length;
            if (len == 0)
            {
                return;
            }

            var parents = new BaseItem[len];
            for (int i = 0; i < len; i++)
            {
                parents[i] = GetItemById(ancestorIds[i]) ?? throw new ArgumentException($"Failed to find parent with id: {ancestorIds[i]}");
                if (parents[i] is not (ICollectionFolder or UserView))
                {
                    return;
                }
            }

            // Optimize by querying against top level views
            var topParentIds = parents.SelectMany(i => GetTopParentIdsForQuery(i, query.User)).ToArray();
            if (topParentIds.Length == 0)
            {
                return;
            }

            query.TopParentIds = topParentIds;
            query.AncestorIds = [];
        }

        public QueryResult<(BaseItem Item, ItemCounts ItemCounts)> GetAlbumArtists(InternalItemsQuery query)
        {
            if (query.User is not null)
            {
                AddUserToQuery(query, query.User);
            }

            SetTopParentOrAncestorIds(query);
            return _itemRepository.GetAlbumArtists(query);
        }

        public QueryResult<BaseItem> GetItemsResult(InternalItemsQuery query)
        {
            SetTopParentIdsFromParentId(query);

            return QueryItemsForUser(query);
        }

        private QueryResult<BaseItem> QueryItemsForUser(InternalItemsQuery query)
        {
            if (query.User is not null)
            {
                AddUserToQuery(query, query.User);
            }

            if (query.EnableTotalRecordCount)
            {
                return _itemRepository.GetItems(query);
            }

            return new QueryResult<BaseItem>(
                query.StartIndex,
                null,
                _itemRepository.GetItemList(query));
        }

        private void SetTopParentIdsFromParentId(InternalItemsQuery query)
        {
            if (query.Recursive && !query.ParentId.IsEmpty())
            {
                var parent = GetItemById(query.ParentId);
                if (parent is not null)
                {
                    SetTopParentIdsOrAncestors(query, [parent]);
                }
            }
        }

        private void ScopeQueryToParents(InternalItemsQuery query, IReadOnlyCollection<BaseItem> parents)
        {
            SetTopParentIdsOrAncestors(query, parents);

            if (query.AncestorIds.Length == 0 && query.TopParentIds.Length == 0)
            {
                if (query.User is not null)
                {
                    AddUserToQuery(query, query.User);
                }
            }
        }

        private void SetTopParentIdsOrAncestors(InternalItemsQuery query, IReadOnlyCollection<BaseItem> parents)
        {
            if (parents.All(i => i is ICollectionFolder || i is UserView))
            {
                // Optimize by querying against top level views
                var topParentIds = parents.SelectMany(i => GetTopParentIdsForQuery(i, query.User)).ToArray();

                if (topParentIds.Length > 0)
                {
                    query.TopParentIds = topParentIds;
                }
                else
                {
                    SetAncestorIds(query, parents);
                }
            }
            else if (TryGetSingleParentFolder(parents, out var folder)
                && IsLinkedChildrenContainer(folder))
            {
                // Playlists and BoxSets store their contents in LinkedChildren and never
                // populate AncestorIds for those items, so a recursive AncestorIds query
                // would return zero rows. Filter by the descendant set instead, which follows
                // the links and keeps descending, so a linked folder contributes what is below
                // it as well - the episodes of a Series added to a collection, for example.
                query.DescendantOfId = folder.Id;
            }
            else
            {
                SetAncestorIds(query, parents);
            }

            query.Parent = null;
        }

        private static bool TryGetSingleParentFolder(IReadOnlyCollection<BaseItem> parents, [NotNullWhen(true)] out Folder? folder)
        {
            folder = parents.Count == 1 ? parents.First() as Folder : null;
            return folder is not null;
        }

        private static bool IsLinkedChildrenContainer(Folder folder)
        {
            return (folder is Playlist || folder is BoxSet)
                && folder.LinkedChildren.Length > 0;
        }

        private static void SetAncestorIds(InternalItemsQuery query, IReadOnlyCollection<BaseItem> parents)
        {
            // We need to be able to query from any arbitrary ancestor up the tree
            query.AncestorIds = parents.SelectMany(i => i.GetIdsForAncestorQuery()).ToArray();

            // Prevent searching in all libraries due to empty filter
            if (query.AncestorIds.Length == 0)
            {
                query.AncestorIds = [Guid.NewGuid()];
            }
        }

        private void AddUserToQuery(InternalItemsQuery query, User user, bool allowExternalContent = true)
        {
            if (query.User is null)
            {
                query.SetUser(user);
            }

            if (HasNoParentFilter(query))
            {
                var userViews = UserViewManager.GetUserViews(new UserViewQuery
                {
                    User = user,
                    IncludeHidden = true,
                    IncludeExternalContent = allowExternalContent
                });

                query.TopParentIds = userViews.SelectMany(i => GetTopParentIdsForQuery(i, user)).ToArray();

                // Prevent searching in all libraries due to empty filter
                if (query.TopParentIds.Length == 0)
                {
                    query.TopParentIds = [Guid.NewGuid()];
                }
            }
        }

        private static bool HasNoParentFilter(InternalItemsQuery query)
        {
            return query.AncestorIds.Length == 0 &&
                query.ParentId.IsEmpty() &&
                query.ChannelIds.Count == 0 &&
                query.TopParentIds.Length == 0 &&
                string.IsNullOrEmpty(query.AncestorWithPresentationUniqueKey) &&
                string.IsNullOrEmpty(query.SeriesPresentationUniqueKey) &&
                query.ItemIds.Length == 0 &&
                query.OwnerIds.Length == 0;
        }

        /// <inheritdoc/>
        public void ConfigureUserAccess(InternalItemsQuery query, User user)
        {
            ArgumentNullException.ThrowIfNull(query);
            ArgumentNullException.ThrowIfNull(user);

            AddUserToQuery(query, user);
        }

        private IEnumerable<Guid> GetTopParentIdsForQuery(BaseItem item, User? user)
        {
            if (item is UserView view)
            {
                return GetTopParentIdsForView(view, user);
            }

            if (item is CollectionFolder collectionFolder)
            {
                return collectionFolder.PhysicalFolderIds;
            }

            var topParent = item.GetTopParent();
            if (topParent is not null)
            {
                return [topParent.Id];
            }

            return [];
        }

        private IEnumerable<Guid> GetTopParentIdsForView(UserView view, User? user)
        {
            if (view.ViewType == CollectionType.livetv)
            {
                return [view.Id];
            }

            // Translate view into folders
            if (!view.DisplayParentId.IsEmpty())
            {
                return GetTopParentIdsForParent(view.DisplayParentId, user);
            }

            if (!view.ParentId.IsEmpty())
            {
                return GetTopParentIdsForParent(view.ParentId, user);
            }

            // Handle grouping
            if (user is not null && IsGroupedView(view, user))
            {
                return GetUserRootFolder()
                    .GetChildren(user, true)
                    .OfType<CollectionFolder>()
                    .Where(i => i.CollectionType is null || i.CollectionType == view.ViewType)
                    .Where(i => user.IsFolderGrouped(i.Id))
                    .SelectMany(i => GetTopParentIdsForQuery(i, user));
            }

            return [];
        }

        private IEnumerable<Guid> GetTopParentIdsForParent(Guid parentId, User? user)
        {
            var displayParent = GetItemById(parentId);
            if (displayParent is not null)
            {
                return GetTopParentIdsForQuery(displayParent, user);
            }

            return [];
        }

        private static bool IsGroupedView(UserView view, User user)
        {
            return view.ViewType != CollectionType.unknown && UserView.IsEligibleForGrouping(view.ViewType)
                && user.GetPreference(PreferenceKind.GroupedFolders).Length > 0;
        }

        /// <summary>
        /// Retrieves the item.
        /// </summary>
        /// <param name="id">The id.</param>
        /// <returns>BaseItem.</returns>
        public BaseItem RetrieveItem(Guid id)
        {
            return _itemRepository.RetrieveItem(id);
        }

        public List<Folder> GetCollectionFolders(BaseItem item)
        {
            return GetCollectionFolders(item, GetUserRootFolder().Children.OfType<Folder>());
        }

        public List<Folder> GetCollectionFolders(BaseItem item, IEnumerable<Folder> allUserRootChildren)
        {
            while (item is not null)
            {
                var result = StepTowardCollectionRoot(new CollectionRootStep(item, false));
                item = result.Item;
                if (result.Stopped)
                {
                    break;
                }
            }

            if (item is null)
            {
                return [];
            }

            return GetCollectionFoldersInternal(item, allUserRootChildren);
        }

        private static CollectionRootStep StepTowardCollectionRoot(CollectionRootStep current)
        {
            var item = current.Item;
            var parent = item.GetParent();

            if (parent is AggregateFolder)
            {
                return new CollectionRootStep(item, true);
            }

            if (parent is null)
            {
                var owner = item.GetOwner();

                if (owner is null)
                {
                    return new CollectionRootStep(item, true);
                }

                item = owner;
            }
            else
            {
                item = parent;
            }

            return new CollectionRootStep(item, false);
        }

        private static List<Folder> GetCollectionFoldersInternal(BaseItem item, IEnumerable<Folder> allUserRootChildren)
        {
            return allUserRootChildren
                .Where(i => string.Equals(i.Path, item.Path, StringComparison.OrdinalIgnoreCase) || i.PhysicalLocations.Contains(item.Path.AsSpan(), StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        public BaseItem GetParentItem(Guid? parentId, Guid? userId)
        {
            if (parentId.HasValue)
            {
                return GetItemById(parentId.Value) ?? throw new ArgumentException($"Invalid parent id: {parentId.Value}");
            }

            if (!userId.IsNullOrEmpty())
            {
                return GetUserRootFolder();
            }

            return RootFolder;
        }

        /// <inheritdoc />
        public void QueueLibraryScan()
        {
            _taskManager.QueueScheduledTask<RefreshMediaLibraryTask>();
        }

        public ItemLookupInfo ParseName(string name)
        {
            var namingOptions = _namingOptions;
            var result = VideoResolver.CleanDateTime(name, namingOptions);

            return new ItemLookupInfo
            {
                Name = VideoResolver.TryCleanString(result.Name, namingOptions, out var newName) ? newName : result.Name,
                Year = result.Year
            };
        }

        public string GetPathAfterNetworkSubstitution(string path, BaseItem? ownerItem)
        {
            foreach (var map in _configurationManager.Configuration.PathSubstitutions)
            {
                if (path.TryReplaceSubPath(map.From, map.To, out var newPath))
                {
                    return newPath;
                }
            }

            return path;
        }

        private static bool ItemIsVisible(BaseItem? item, User? user)
        {
            if (item is null)
            {
                return false;
            }

            if (user is null)
            {
                return true;
            }

            return item is UserRootFolder || item.IsVisibleStandalone(user);
        }

        /// <inheritdoc />
        public async Task RerouteLinkedChildReferencesAsync(Guid fromChildId, Guid toChildId)
        {
            var affectedParentIds = _linkedChildrenService.RerouteLinkedChildren(fromChildId, toChildId);

            // Update in-memory LinkedChildren and re-save metadata (NFO) for affected parents
            foreach (var parentId in affectedParentIds)
            {
                if (GetItemById(parentId) is not Folder parent)
                {
                    continue;
                }

                RerouteInMemoryLinkedChildren(parent, fromChildId, toChildId);

                await RunMetadataSavers(parent, ItemUpdateType.MetadataEdit).ConfigureAwait(false);
            }
        }

        private static void RerouteInMemoryLinkedChildren(Folder parent, Guid fromChildId, Guid toChildId)
        {
            foreach (var lc in parent.LinkedChildren)
            {
                if (lc.ItemId.HasValue && lc.ItemId.Value.Equals(fromChildId))
                {
                    lc.ItemId = toChildId;
                }
            }
        }

        /// <inheritdoc />
        public QueryFiltersLegacy GetQueryFiltersLegacy(InternalItemsQuery query)
        {
            if (query.User is not null)
            {
                AddUserToQuery(query, query.User);
            }

            SetTopParentOrAncestorIds(query);
            return _itemRepository.GetQueryFiltersLegacy(query);
        }

        /// <inheritdoc />
        public IReadOnlyList<string> GetTagNames(InternalItemsQuery query)
        {
            if (query.User is not null)
            {
                AddUserToQuery(query, query.User);
            }

            SetTopParentOrAncestorIds(query);
            return _itemRepository.GetTagNames(query);
        }

        /// <inheritdoc />
        public IReadOnlyList<string> GetMediaStreamLanguages(MediaStreamType mediaStreamType)
        {
            return _mediaStreamRepository.GetMediaStreamLanguages(mediaStreamType);
        }

        /// <inheritdoc />
        public IReadOnlyList<string> GetMediaStreamLanguages(MediaStreamType mediaStreamType, InternalItemsQuery query)
        {
            if (query.User is not null)
            {
                AddUserToQuery(query, query.User);
            }

            SetTopParentOrAncestorIds(query);
            return _itemRepository.GetMediaStreamLanguages(query, mediaStreamType);
        }

        private readonly record struct CollectionRootStep(BaseItem Item, bool Stopped);
    }
}
