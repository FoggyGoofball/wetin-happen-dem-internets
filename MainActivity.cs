using Android.App;
using Android.Content;
using Android.OS;
using Android.Widget;
using Android.Views;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace wetin_happen_dem_internets
{
    [Activity(Label = "@string/app_name", MainLauncher = true)]
    [IntentFilter(
        [Intent.ActionSend],
        Categories = [Intent.CategoryDefault],
        DataMimeType = "text/plain")]
    public class MainActivity : Activity
    {
        private EditText? _urlInput;
        private EditText? _tagsInput;
        private Button? _fetchButton;
        private Button? _saveButton;
        private Button? _shareButton;
        private Button? _uploadDocsButton;
        private TextView? _titleView;
        private TextView? _citationView;
        private TextView? _contentView;
        private TextView? _statusView;
        private TextView? _diagnosticView;
        private ProgressBar? _progressBar;
        private Android.Widget.ImageView? _heroImageView;
        private Switch? _sideBySideSwitch;
        private Spinner? _savedStoriesSpinner;
        private Button? _loadSavedButton;
        private EditText? _feedUrlInput;
        private Button? _refreshFeedButton;
        private ListView? _articlesListView;
        private EditText? _dbSearchInput;
        private Button? _loadDbButton;
        private ListView? _dbArticlesListView;

        private readonly List<StoryRecord> _savedStories = [];
        private readonly Dictionary<string, StoryRecord> _sessionStoryCache = new(StringComparer.OrdinalIgnoreCase);
        private StoryRecord? _currentStory;
        private readonly List<FeedArticle> _feedArticles = [];
        private readonly List<IndexItem> _dbIndexItems = [];
        private readonly List<IndexItem> _filteredDbIndexItems = [];

        private const string SavedStoriesKey = "saved_stories";
        private const string DefaultGoogleNewsFeedUrl = "https://news.google.com/rss?hl=en-NG&gl=NG&ceid=NG:en";

        // GitHub DB settings (persisted locally)
        private const string DbSettingsPref = "github_db_settings";
        private const string DbOwnerKey = "owner";
        private const string DbRepoKey = "repo";
        private const string DbBranchKey = "branch";
        private const string DbTokenKey = "token";
        private const string DbSpaBaseUrlKey = "spa_base_url";

        private const string DefaultDbOwner = "FoggyGoofball";
        private const string DefaultDbRepo = "wetin-happen-db";
        private const string DefaultDbBranch = "main";
        private const string DefaultSpaBaseUrl = "https://foggygoofball.github.io/wetin-happen-spa";

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            SetContentView(Resource.Layout.activity_main);

            _urlInput = FindViewById<EditText>(Resource.Id.urlInput);
            _tagsInput = FindViewById<EditText>(Resource.Id.tagsInput);
            _fetchButton  = FindViewById<Button>(Resource.Id.fetchButton);
            _saveButton   = FindViewById<Button>(Resource.Id.saveButton);
            _shareButton  = FindViewById<Button>(Resource.Id.shareButton);
            _uploadDocsButton = FindViewById<Button>(Resource.Id.uploadDocsButton);
            _titleView = FindViewById<TextView>(Resource.Id.titleView);
            _citationView = FindViewById<TextView>(Resource.Id.citationView);
            _contentView = FindViewById<TextView>(Resource.Id.contentView);
            _statusView = FindViewById<TextView>(Resource.Id.statusView);
            _diagnosticView = FindViewById<TextView>(Resource.Id.diagnosticView);
            _progressBar = FindViewById<ProgressBar>(Resource.Id.progressBar);
            _heroImageView = FindViewById<Android.Widget.ImageView>(Resource.Id.heroImageView);
            _sideBySideSwitch = FindViewById<Switch>(Resource.Id.sideBySideSwitch);
            _savedStoriesSpinner = FindViewById<Spinner>(Resource.Id.savedStoriesSpinner);
            _loadSavedButton = FindViewById<Button>(Resource.Id.loadSavedButton);
            _feedUrlInput = FindViewById<EditText>(Resource.Id.feedUrlInput);
            _refreshFeedButton = FindViewById<Button>(Resource.Id.refreshFeedButton);
            _articlesListView = FindViewById<ListView>(Resource.Id.articlesListView);
            _dbSearchInput = FindViewById<EditText>(Resource.Id.dbSearchInput);
            _loadDbButton = FindViewById<Button>(Resource.Id.loadDbButton);
            _dbArticlesListView = FindViewById<ListView>(Resource.Id.dbArticlesListView);

            _fetchButton!.Click += async (_, _) => await FetchAndTranslateAsync();
            _saveButton!.Click  += (_, _) => SaveCurrentStory();
            _shareButton!.Click += async (_, _) => await ShareCurrentStoryAsync();
            _uploadDocsButton!.Click += async (_, _) => await SaveToGithubDbAsync();
            _uploadDocsButton!.LongClick += (_, _) => ShowDbSettingsDialog();
            _sideBySideSwitch!.CheckedChange += (_, _) => RenderStory();
            _loadSavedButton!.Click += (_, _) => LoadSelectedStory();
            _refreshFeedButton!.Click += async (_, _) => await RefreshFeedArticlesAsync();
            _articlesListView!.ItemClick += async (_, args) =>
            {
                if (args.Position < 0 || args.Position >= _feedArticles.Count)
                {
                    return;
                }

                var selected = _feedArticles[args.Position];
                _urlInput!.Text = selected.Url;
                await FetchAndTranslateAsync();
            };

            _dbSearchInput!.TextChanged += (_, _) => FilterDbArticles();
            _loadDbButton!.Click += async (_, _) => await LoadDbIndexAsync();
            _dbArticlesListView!.ItemClick += (_, args) =>
            {
                if (args.Position < 0 || args.Position >= _filteredDbIndexItems.Count)
                {
                    return;
                }

                _ = LoadDbArticleAsync(_filteredDbIndexItems[args.Position]);
            };

            _feedUrlInput!.Text = DefaultGoogleNewsFeedUrl;

            LoadSavedStoriesFromStorage();
            BindSavedStories();
            BindFeedArticles();
            BindDbArticles();
            SetIdleState(GetString(Resource.String.status_ready));

            _ = RefreshFeedArticlesAsync();
            _ = HandleIncomingShareIntentAsync(Intent);
        }

        protected override void OnNewIntent(Intent? intent)
        {
            base.OnNewIntent(intent);
            if (intent is null)
            {
                return;
            }

            _ = HandleIncomingShareIntentAsync(intent);
        }

        private async Task HandleIncomingShareIntentAsync(Intent? intent)
        {
            if (intent is null || intent.Action != Intent.ActionSend)
            {
                return;
            }

            var sharedText = intent.GetStringExtra(Intent.ExtraText);
            if (string.IsNullOrWhiteSpace(sharedText))
            {
                return;
            }

            var url = ExtractFirstUrl(sharedText);
            if (url is null)
            {
                return;
            }

            _urlInput!.Text = url;
            SetIdleState(GetString(Resource.String.status_fetching));
            await FetchAndTranslateAsync();
        }

        private static string? ExtractFirstUrl(string text)
        {
            var match = Regex.Match(text, @"https?://\S+", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return null;
            }

            return match.Value.TrimEnd('.', ',', ';', ')', ']', '"', '\'');
        }

        private async Task FetchAndTranslateAsync()
        {
            var urlText = _urlInput?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(urlText) || !Uri.TryCreate(urlText, UriKind.Absolute, out var inputUrl))
            {
                Toast.MakeText(this, Resource.String.error_invalid_url, ToastLength.Short)?.Show();
                UpdateDiagnostic(GetString(Resource.String.error_invalid_url), true);
                return;
            }

            SetBusyState(GetString(Resource.String.status_fetching));

            try
            {
                using var extractor = new ArticleExtractor(this);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(28));
                var webArticle = await extractor.ExtractAsync(inputUrl, cts.Token);

                if (webArticle is null || webArticle.Paragraphs.Count == 0)
                {
                    if (TryLoadCachedStory(inputUrl.ToString(), out var cached))
                    {
                        _currentStory = cached;
                        RenderStory();
                        SetIdleState(GetString(Resource.String.status_loaded_cached));
                        UpdateDiagnostic(GetString(Resource.String.diagnostic_cached_used), false);
                        return;
                    }

                    SetIdleState(GetString(Resource.String.error_article_parse));
                    UpdateDiagnostic(GetString(Resource.String.diagnostic_parse), true);
                    return;
                }

                var resolvedUrl = inputUrl;
                _urlInput!.Text = resolvedUrl.ToString();

                var translationResult = await Task.Run(() =>
                {
                    var output = new List<ParagraphPair>();
                    foreach (var paragraph in webArticle.Paragraphs)
                    {
                        output.Add(new ParagraphPair(paragraph, PidginTranslator.Translate(paragraph)));
                    }
                    return output;
                });

                var tags = ParseTags(_tagsInput?.Text);

                _currentStory = new StoryRecord
                {
                    Title = webArticle.Title,
                    SourceUrl = inputUrl.ToString(),
                    SourceDomain = inputUrl.Host,
                    HeroImageUrl = webArticle.ImageUrl ?? string.Empty,
                    Tags = tags,
                    Paragraphs = translationResult,
                    SavedAtUtc = DateTimeOffset.UtcNow
                };

                _sessionStoryCache[inputUrl.ToString()] = _currentStory;

                RenderStory();
                SetIdleState(GetString(Resource.String.status_done));
                UpdateDiagnostic(GetString(Resource.String.diagnostic_none), false);
            }
            catch (System.OperationCanceledException)
            {
                if (TryLoadCachedStory(inputUrl.ToString(), out var cached))
                {
                    _currentStory = cached;
                    RenderStory();
                    SetIdleState(GetString(Resource.String.status_loaded_cached));
                    UpdateDiagnostic(GetString(Resource.String.diagnostic_cached_used), false);
                    return;
                }

                SetIdleState(GetString(Resource.String.error_fetch_failed));
                UpdateDiagnostic(GetString(Resource.String.diagnostic_timeout), true);
            }
            catch (Exception ex)
            {
                if (TryLoadCachedStory(inputUrl.ToString(), out var cached))
                {
                    _currentStory = cached;
                    RenderStory();
                    SetIdleState(GetString(Resource.String.status_loaded_cached));
                    UpdateDiagnostic(GetString(Resource.String.diagnostic_cached_used), false);
                    return;
                }

                SetIdleState(GetString(Resource.String.error_fetch_failed));
                Toast.MakeText(this, ex.Message, ToastLength.Long)?.Show();
                UpdateDiagnostic($"Why: {ex.GetType().Name} — {ex.Message}", true);
            }
        }

        private static HttpClient CreateArticleHttpClient()
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
            };

            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Mobile Safari/537.36");
            client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
            return client;
        }

        private static async Task<Uri> ResolveArticleUrlAsync(HttpClient client, Uri inputUrl)
        {
            if (inputUrl.Host.Contains("news.google.com", StringComparison.OrdinalIgnoreCase))
            {
                var qpTarget = ExtractQueryStringUrl(inputUrl);
                if (qpTarget is not null)
                {
                    return qpTarget;
                }
            }

            using var req = new HttpRequestMessage(HttpMethod.Get, inputUrl);
            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            var finalUri = resp.RequestMessage?.RequestUri;
            return finalUri ?? inputUrl;
        }

        private static Uri? ExtractQueryStringUrl(Uri uri)
        {
            var query = uri.Query;
            if (string.IsNullOrWhiteSpace(query))
            {
                return null;
            }

            var keys = new[] { "url", "u", "q" };
            var pairs = query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
            foreach (var pair in pairs)
            {
                var split = pair.Split('=', 2);
                if (split.Length != 2)
                {
                    continue;
                }

                var key = Uri.UnescapeDataString(split[0]);
                if (!keys.Contains(key, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                var value = Uri.UnescapeDataString(split[1]);
                if (!string.IsNullOrWhiteSpace(value) && Uri.TryCreate(value, UriKind.Absolute, out var outUri))
                {
                    return outUri;
                }
            }

            return null;
        }

        private void RenderStory()
        {
            if (_currentStory is null)
            {
                _titleView!.Text = GetString(Resource.String.placeholder_title);
                _citationView!.Text = GetString(Resource.String.placeholder_citation);
                _contentView!.Text = string.Empty;
                _heroImageView!.Visibility = ViewStates.Gone;
                return;
            }

            _titleView!.Text = _currentStory.Title;
            var tagText = _currentStory.Tags.Count > 0 ? $" • tags: {string.Join(", ", _currentStory.Tags)}" : string.Empty;
            _citationView!.Text = string.Format(GetString(Resource.String.citation_template), _currentStory.SourceDomain, _currentStory.SourceUrl) + tagText;

            _heroImageView!.Visibility = ViewStates.Gone;
            if (!string.IsNullOrWhiteSpace(_currentStory.HeroImageUrl))
                _ = LoadHeroImageAsync(_currentStory.HeroImageUrl);

            var sideBySide = _sideBySideSwitch?.Checked ?? false;

            if (sideBySide)
            {
                var spannable = new Android.Text.SpannableStringBuilder();

                foreach (var p in _currentStory.Paragraphs)
                {
                    // "English" label — bold + colour
                    AppendSpanned(spannable, "English\n",
                        new Android.Text.Style.StyleSpan(Android.Graphics.TypefaceStyle.Bold),
                        new Android.Text.Style.ForegroundColorSpan(Android.Graphics.Color.ParseColor("#6B7280")));

                    // English body — bold
                    AppendSpanned(spannable, p.English + "\n",
                        new Android.Text.Style.StyleSpan(Android.Graphics.TypefaceStyle.Bold));

                    spannable.Append("\n");

                    // "Pidgin" label — italic + colour
                    AppendSpanned(spannable, "Pidgin\n",
                        new Android.Text.Style.StyleSpan(Android.Graphics.TypefaceStyle.Italic),
                        new Android.Text.Style.ForegroundColorSpan(Android.Graphics.Color.ParseColor("#6B7280")));

                    // Pidgin body — italic
                    AppendSpanned(spannable, p.Pidgin + "\n",
                        new Android.Text.Style.StyleSpan(Android.Graphics.TypefaceStyle.Italic));

                    spannable.Append("\n");
                }

                _contentView!.SetText(spannable, Android.Widget.TextView.BufferType.Spannable);
            }
            else
            {
                _contentView!.Text = string.Join("\n\n", _currentStory.Paragraphs.Select(p => p.Pidgin)).TrimEnd();
            }
        }

        private static void AppendSpanned(
            Android.Text.SpannableStringBuilder sb,
            string text,
            params Java.Lang.Object[] spans)
        {
            var start = sb.Length();
            sb.Append(text);
            var end = sb.Length();
            foreach (var span in spans)
            {
                sb.SetSpan(span, start, end, Android.Text.SpanTypes.ExclusiveExclusive);
            }
        }

        private async Task LoadHeroImageAsync(string imageUrl)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 Chrome/124.0 Mobile Safari/537.36");

                var bytes = await client.GetByteArrayAsync(imageUrl);
                var bitmap = await Android.Graphics.BitmapFactory.DecodeByteArrayAsync(bytes, 0, bytes.Length);

                if (bitmap is not null)
                {
                    RunOnUiThread(() =>
                    {
                        _heroImageView!.SetImageBitmap(bitmap);
                        _heroImageView.Visibility = ViewStates.Visible;
                    });
                }
            }
            catch
            {
                RunOnUiThread(() => _heroImageView!.Visibility = ViewStates.Gone);
            }
        }

        private void SaveCurrentStory()
        {
            if (_currentStory is null)
            {
                Toast.MakeText(this, Resource.String.error_nothing_to_save, ToastLength.Short)?.Show();
                return;
            }

            _currentStory = _currentStory with { Tags = ParseTags(_tagsInput?.Text) };

            var existing = _savedStories.FindIndex(s => string.Equals(s.SourceUrl, _currentStory.SourceUrl, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
            {
                _savedStories[existing] = _currentStory with { SavedAtUtc = DateTimeOffset.UtcNow };
            }
            else
            {
                _savedStories.Insert(0, _currentStory with { SavedAtUtc = DateTimeOffset.UtcNow });
            }

            PersistSavedStories();
            _sessionStoryCache[_currentStory.SourceUrl] = _currentStory;
            BindSavedStories();
            Toast.MakeText(this, Resource.String.status_saved, ToastLength.Short)?.Show();
        }

        private void LoadSelectedStory()
        {
            if (_savedStoriesSpinner is null || _savedStories.Count == 0)
            {
                Toast.MakeText(this, Resource.String.error_no_saved_story, ToastLength.Short)?.Show();
                return;
            }

            var index = _savedStoriesSpinner.SelectedItemPosition;
            if (index < 0 || index >= _savedStories.Count)
            {
                return;
            }

            _currentStory = _savedStories[index];
            _urlInput!.Text = _currentStory.SourceUrl;
            _tagsInput!.Text = string.Join(", ", _currentStory.Tags);
            RenderStory();
            SetIdleState(GetString(Resource.String.status_loaded_saved));
        }

        private async Task ShareCurrentStoryAsync()
        {
            if (_currentStory is null)
            {
                Toast.MakeText(this, Resource.String.error_nothing_to_share, ToastLength.Short)?.Show();
                return;
            }

            var settings = GetDbSettings();
            var index = await LoadPublicIndexAsync(settings);
            var existing = index.FirstOrDefault(i => string.Equals(i.SourceUrl, _currentStory.SourceUrl, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                var shouldSubmit = await PromptSubmitBeforeShareAsync();
                if (!shouldSubmit)
                {
                    return;
                }

                await SaveToGithubDbAsync();
                index = await LoadPublicIndexAsync(settings);
                existing = index.FirstOrDefault(i => string.Equals(i.SourceUrl, _currentStory.SourceUrl, StringComparison.OrdinalIgnoreCase));

                if (existing is null)
                {
                    Toast.MakeText(this, Resource.String.github_db_save_failed, ToastLength.Long)?.Show();
                    return;
                }
            }

            var spaLink = $"{settings.SpaBaseUrl.TrimEnd('/')}/?id={Uri.EscapeDataString(existing.Id)}";
            var sendIntent = new Intent(Intent.ActionSend);
            sendIntent.SetType("text/plain");
            sendIntent.PutExtra(Intent.ExtraTitle, _currentStory.Title);
            sendIntent.PutExtra(Intent.ExtraText, spaLink);
            StartActivity(Intent.CreateChooser(sendIntent, GetString(Resource.String.share_chooser_title)));
        }

        private Task<bool> PromptSubmitBeforeShareAsync()
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            new AlertDialog.Builder(this)
                .SetTitle(GetString(Resource.String.share_submit_required_title))
                .SetMessage(GetString(Resource.String.share_submit_required_message))
                .SetNegativeButton(GetString(Android.Resource.String.Cancel), (_, _) => tcs.TrySetResult(false))
                .SetPositiveButton(GetString(Resource.String.upload_docs_button), (_, _) => tcs.TrySetResult(true))
                .SetOnCancelListener(new DialogCancelListener(() => tcs.TrySetResult(false)))
                .Show();

            return tcs.Task;
        }

        private async Task<List<IndexItem>> LoadPublicIndexAsync(DbSettings settings)
        {
            try
            {
                var url = $"https://raw.githubusercontent.com/{settings.Owner}/{settings.Repo}/{settings.Branch}/data/index.json?{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
                var json = await new HttpClient().GetStringAsync(url);
                return JsonSerializer.Deserialize<List<IndexItem>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
            }
            catch
            {
                return [];
            }
        }

        private sealed class DialogCancelListener : Java.Lang.Object, IDialogInterfaceOnCancelListener
        {
            private readonly Action _onCancel;
            public DialogCancelListener(Action onCancel) => _onCancel = onCancel;
            public void OnCancel(IDialogInterface? dialog) => _onCancel();
        }

        private async Task RefreshFeedArticlesAsync()
        {
            var feedUrl = _feedUrlInput?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(feedUrl) || !Uri.TryCreate(feedUrl, UriKind.Absolute, out var parsedFeedUrl))
            {
                Toast.MakeText(this, Resource.String.error_invalid_url, ToastLength.Short)?.Show();
                UpdateDiagnostic(GetString(Resource.String.error_invalid_url), true);
                return;
            }

            SetBusyState(GetString(Resource.String.feed_status_refreshing));

            try
            {
                using var client = CreateArticleHttpClient();
                var xml = await client.GetStringAsync(parsedFeedUrl);

                var items = ParseRssFeed(xml)
                    .Take(30)
                    .ToList();

                _feedArticles.Clear();
                _feedArticles.AddRange(items);
                BindFeedArticles();

                SetIdleState(_feedArticles.Count == 0
                    ? GetString(Resource.String.feed_empty)
                    : GetString(Resource.String.feed_status_loaded));
                UpdateDiagnostic(GetString(Resource.String.diagnostic_none), false);
            }
            catch
            {
                SetIdleState(GetString(Resource.String.error_feed_fetch_failed));
                UpdateDiagnostic(GetString(Resource.String.diagnostic_feed), true);
            }
        }

        private void BindFeedArticles()
        {
            if (_articlesListView is null)
            {
                return;
            }

            var items = _feedArticles.Count == 0
                ? [GetString(Resource.String.feed_empty)]
                : _feedArticles.Select(a => $"{a.Title}\n{a.Source}").ToList();

            var adapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleListItem1, items);
            _articlesListView.Adapter = adapter;
        }

        private static IEnumerable<FeedArticle> ParseRssFeed(string xml)
        {
            var doc = XDocument.Parse(xml);
            var items = doc.Descendants("item");

            foreach (var item in items)
            {
                var title = WebUtility.HtmlDecode((item.Element("title")?.Value ?? string.Empty).Trim());
                var link = (item.Element("link")?.Value ?? string.Empty).Trim();
                var source = WebUtility.HtmlDecode((item.Element("source")?.Value ?? "Google News").Trim());

                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(link))
                {
                    continue;
                }

                yield return new FeedArticle(title, link, source);
            }
        }

        private void BindSavedStories()
        {
            if (_savedStoriesSpinner is null)
            {
                return;
            }

            var items = _savedStories
                .Select(s => $"{s.Title} ({s.SavedAtUtc.LocalDateTime:g})")
                .ToList();

            if (items.Count == 0)
            {
                items.Add(GetString(Resource.String.saved_list_empty));
            }

            var adapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSpinnerDropDownItem, items);
            _savedStoriesSpinner.Adapter = adapter;
            _loadSavedButton!.Enabled = _savedStories.Count > 0;
        }

        private static List<string> ParseTags(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return [];
            }

            return text.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim().ToLowerInvariant())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct()
                .Take(12)
                .ToList();
        }

        private async Task LoadDbIndexAsync()
        {
            var settings = GetDbSettings();
            if (string.IsNullOrWhiteSpace(settings.Token))
            {
                Toast.MakeText(this, Resource.String.github_db_not_configured, ToastLength.Long)?.Show();
                return;
            }

            try
            {
                SetBusyState(GetString(Resource.String.github_db_saving));
                var (items, _) = await LoadIndexAsync(settings);
                _dbIndexItems.Clear();
                _dbIndexItems.AddRange(items);
                BindDbArticles();
                SetIdleState(GetString(Resource.String.status_ready));
            }
            catch
            {
                SetIdleState(GetString(Resource.String.github_db_save_failed));
            }
        }

        private void FilterDbArticles() => BindDbArticles();

        private void BindDbArticles()
        {
            if (_dbArticlesListView is null)
            {
                return;
            }

            var q = (_dbSearchInput?.Text ?? string.Empty).Trim().ToLowerInvariant();
            var filtered = _dbIndexItems
                .Where(i => string.IsNullOrWhiteSpace(q)
                    || i.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || (i.Tags ?? []).Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            _filteredDbIndexItems.Clear();
            _filteredDbIndexItems.AddRange(filtered);

            var items = filtered.Count == 0
                ? [GetString(Resource.String.db_empty)]
                : filtered.Select(i => $"{i.Title}\n{i.SourceDomain} • {string.Join(", ", i.Tags ?? [])}").ToList();

            var adapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleListItem1, items);
            _dbArticlesListView.Adapter = adapter;
        }

        private async Task LoadDbArticleAsync(IndexItem item)
        {
            try
            {
                var rawBase = $"https://raw.githubusercontent.com/{GetDbSettings().Owner}/{GetDbSettings().Repo}/{GetDbSettings().Branch}";
                var json = await new HttpClient().GetStringAsync($"{rawBase}/{item.Path}?{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}");

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var title = root.GetProperty("title").GetString() ?? item.Title;
                var sourceUrl = root.GetProperty("sourceUrl").GetString() ?? item.SourceUrl;
                var sourceDomain = root.GetProperty("sourceDomain").GetString() ?? item.SourceDomain;
                var heroImageUrl = root.TryGetProperty("heroImageUrl", out var heroEl) ? heroEl.GetString() ?? string.Empty : string.Empty;

                var tags = root.TryGetProperty("tags", out var tagsEl) && tagsEl.ValueKind == JsonValueKind.Array
                    ? tagsEl.EnumerateArray().Select(e => e.GetString() ?? string.Empty).Where(s => !string.IsNullOrWhiteSpace(s)).ToList()
                    : (item.Tags ?? []);

                var paragraphs = new List<ParagraphPair>();
                if (root.TryGetProperty("paragraphs", out var pEl) && pEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var p in pEl.EnumerateArray())
                    {
                        var en = p.TryGetProperty("english", out var enEl) ? enEl.GetString() ?? string.Empty : string.Empty;
                        var pcm = p.TryGetProperty("pidgin", out var pcmEl) ? pcmEl.GetString() ?? string.Empty : string.Empty;
                        if (!string.IsNullOrWhiteSpace(pcm))
                        {
                            paragraphs.Add(new ParagraphPair(en, pcm));
                        }
                    }
                }

                _currentStory = new StoryRecord
                {
                    Title = title,
                    SourceUrl = sourceUrl,
                    SourceDomain = sourceDomain,
                    HeroImageUrl = heroImageUrl,
                    Tags = tags,
                    Paragraphs = paragraphs,
                    SavedAtUtc = item.SavedAtUtc
                };

                _urlInput!.Text = sourceUrl;
                _tagsInput!.Text = string.Join(", ", tags);
                RenderStory();
                SetIdleState(GetString(Resource.String.status_done));
            }
            catch
            {
                SetIdleState(GetString(Resource.String.error_fetch_failed));
            }
        }

        private static HttpClient BuildGitHubClient(string token)
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("wetin-happen-dem-internets-app");
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return client;
        }

        private void SetBusyState(string status)
        {
            _progressBar!.Visibility = ViewStates.Visible;
            _fetchButton!.Enabled = false;
            _statusView!.Text = status;
            _diagnosticView!.Visibility = ViewStates.Gone;
        }

        private void SetIdleState(string status)
        {
            _progressBar!.Visibility = ViewStates.Gone;
            _fetchButton!.Enabled = true;
            _statusView!.Text = status;
        }

        private void UpdateDiagnostic(string text, bool isError)
        {
            _diagnosticView!.Text = text;
            _diagnosticView.Visibility = ViewStates.Visible;
            _diagnosticView.SetTextColor(Android.Graphics.Color.ParseColor(isError ? "#B91C1C" : "#166534"));
        }

        private void LoadSavedStoriesFromStorage()
        {
            var prefs = GetPreferences(FileCreationMode.Private);
            var json = prefs?.GetString(SavedStoriesKey, null);
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            try
            {
                var items = JsonSerializer.Deserialize<List<StoryRecord>>(json);
                if (items is { Count: > 0 })
                {
                    _savedStories.Clear();
                    _savedStories.AddRange(items);
                    foreach (var item in items)
                    {
                        if (!string.IsNullOrWhiteSpace(item.SourceUrl))
                        {
                            _sessionStoryCache[item.SourceUrl] = item;
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private void PersistSavedStories()
        {
            var prefs = GetPreferences(FileCreationMode.Private);
            var json = JsonSerializer.Serialize(_savedStories);
            var editor = prefs?.Edit();
            editor?.PutString(SavedStoriesKey, json);
            editor?.Apply();
        }

        private bool TryLoadCachedStory(string url, out StoryRecord story)
        {
            if (_sessionStoryCache.TryGetValue(url, out story!))
                return true;

            var fromSaved = _savedStories.FirstOrDefault(s =>
                string.Equals(s.SourceUrl, url, StringComparison.OrdinalIgnoreCase));
            if (fromSaved is not null)
            {
                story = fromSaved;
                _sessionStoryCache[url] = story;
                return true;
            }

            story = null!;
            return false;
        }

        private async Task SaveToGithubDbAsync()
        {
            if (_currentStory is null)
            {
                Toast.MakeText(this, Resource.String.upload_docs_nothing, ToastLength.Short)?.Show();
                return;
            }

            var settings = GetDbSettings();
            if (string.IsNullOrWhiteSpace(settings.Token))
            {
                Toast.MakeText(this, Resource.String.github_db_not_configured, ToastLength.Long)?.Show();
                ShowDbSettingsDialog();
                return;
            }

            SetBusyState(GetString(Resource.String.github_db_saving));
            _uploadDocsButton!.Enabled = false;

            try
            {
                //await EnsureGitHubWritableAsync(settings);

                var now = DateTimeOffset.UtcNow;

                var tags = ParseTags(_tagsInput?.Text);

                var (indexItems, indexSha) = await LoadIndexAsync(settings);
                var existing = indexItems.FirstOrDefault(i =>
                    string.Equals(i.SourceUrl, _currentStory.SourceUrl, StringComparison.OrdinalIgnoreCase));

                var articleId = existing?.Id ?? $"{now:yyyyMMdd-HHmmss}-{Slugify(_currentStory.Title)}";
                var articlePath = existing?.Path ?? $"data/articles/{articleId}.json";
                var isUpdate = existing is not null;

                var articleSha = isUpdate ? (await GetRepoFileAsync(settings, articlePath)).Sha : null;

                var articleRecord = new
                {
                    id = articleId,
                    title = _currentStory.Title,
                    sourceUrl = _currentStory.SourceUrl,
                    sourceDomain = _currentStory.SourceDomain,
                    heroImageUrl = _currentStory.HeroImageUrl,
                    tags,
                    savedAtUtc = _currentStory.SavedAtUtc,
                    paragraphs = _currentStory.Paragraphs.Select(p => new { english = p.English, pidgin = p.Pidgin }).ToList()
                };

                var articleJson = JsonSerializer.Serialize(articleRecord, new JsonSerializerOptions { WriteIndented = true });
                await PutRepoFileAsync(settings, articlePath, articleJson, $"{(isUpdate ? "Update" : "Add")} article: {_currentStory.Title}", sha: articleSha);

                indexItems.RemoveAll(i => i.Id == articleId || string.Equals(i.SourceUrl, _currentStory.SourceUrl, StringComparison.OrdinalIgnoreCase));
                indexItems.Insert(0, new IndexItem
                {
                    Id = articleId,
                    Title = _currentStory.Title,
                    SourceUrl = _currentStory.SourceUrl,
                    SourceDomain = _currentStory.SourceDomain,
                    HeroImageUrl = _currentStory.HeroImageUrl,
                    Tags = tags,
                    Path = articlePath,
                    SavedAtUtc = now
                });

                var indexJson = JsonSerializer.Serialize(
                    indexItems,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    });
                await PutRepoFileAsync(settings, "data/index.json", indexJson, $"Update index: {articleId}", indexSha);

                var spaLink = $"{settings.SpaBaseUrl.TrimEnd('/')}/?id={Uri.EscapeDataString(articleId)}";
                var clipboard = GetSystemService(ClipboardService) as ClipboardManager;
                if (clipboard is not null)
                {
                    clipboard.PrimaryClip = ClipData.NewPlainText("article_spa_link", spaLink);
                }

                SetIdleState(isUpdate ? GetString(Resource.String.github_db_updated) : GetString(Resource.String.github_db_saved));
                UpdateDiagnostic($"{spaLink}\nSaved in: {settings.Owner}/{settings.Repo}/{articlePath}", false);
                Toast.MakeText(this, isUpdate ? Resource.String.github_db_updated : Resource.String.github_db_saved, ToastLength.Long)?.Show();
            }
            catch (Exception ex)
            {
                SetIdleState(GetString(Resource.String.github_db_save_failed));
                UpdateDiagnostic($"GitHub save failed: {ex.Message}", true);
                Toast.MakeText(this, Resource.String.github_db_save_failed, ToastLength.Long)?.Show();
            }
            finally
            {
                _uploadDocsButton!.Enabled = true;
            }
        }

        private async Task EnsureGitHubWritableAsync(DbSettings settings)
        {
            using var client = BuildGitHubClient(settings.Token);

            // 1) Rate limit check
            var rateJson = await client.GetStringAsync("https://api.github.com/rate_limit");
            using (var rateDoc = JsonDocument.Parse(rateJson))
            {
                var core = rateDoc.RootElement.GetProperty("resources").GetProperty("core");
                var remaining = core.GetProperty("remaining").GetInt32();
                if (remaining <= 0)
                {
                    throw new InvalidOperationException("GitHub API rate limit exhausted. Try again later.");
                }
            }

            // 2) Repo write permission check
            var repoJson = await client.GetStringAsync($"https://api.github.com/repos/{settings.Owner}/{settings.Repo}");
            using var repoDoc = JsonDocument.Parse(repoJson);
            if (repoDoc.RootElement.TryGetProperty("permissions", out var perms))
            {
                var canPush = perms.TryGetProperty("push", out var pushEl) && pushEl.ValueKind == JsonValueKind.True;
                if (!canPush)
                {
                    throw new InvalidOperationException("Token cannot write to this repository (push permission missing).");
                }
            }
        }

        private DbSettings GetDbSettings()
        {
            var prefs = GetSharedPreferences(DbSettingsPref, FileCreationMode.Private);
            var settings = new DbSettings(
                Owner: prefs?.GetString(DbOwnerKey, DefaultDbOwner) ?? DefaultDbOwner,
                Repo: prefs?.GetString(DbRepoKey, DefaultDbRepo) ?? DefaultDbRepo,
                Branch: prefs?.GetString(DbBranchKey, DefaultDbBranch) ?? DefaultDbBranch,
                Token: prefs?.GetString(DbTokenKey, string.Empty) ?? string.Empty,
                SpaBaseUrl: prefs?.GetString(DbSpaBaseUrlKey, DefaultSpaBaseUrl) ?? DefaultSpaBaseUrl);

            // Backward-compat migration: older builds defaulted to app repo, not DB repo.
            if (string.Equals(settings.Repo, "wetin-happen-dem-internets", StringComparison.OrdinalIgnoreCase))
            {
                settings = settings with { Repo = DefaultDbRepo, Branch = DefaultDbBranch, SpaBaseUrl = DefaultSpaBaseUrl };
                SaveDbSettings(settings);
            }

            return settings;
        }

        private void SaveDbSettings(DbSettings settings)
        {
            var prefs = GetSharedPreferences(DbSettingsPref, FileCreationMode.Private);
            var editor = prefs?.Edit();
            editor?.PutString(DbOwnerKey, settings.Owner.Trim());
            editor?.PutString(DbRepoKey, settings.Repo.Trim());
            editor?.PutString(DbBranchKey, settings.Branch.Trim());
            editor?.PutString(DbTokenKey, settings.Token.Trim());
            editor?.PutString(DbSpaBaseUrlKey, settings.SpaBaseUrl.Trim());
            editor?.Apply();
        }

        private void ShowDbSettingsDialog()
        {
            var settings = GetDbSettings();

            var root = new LinearLayout(this)
            {
                Orientation = Orientation.Vertical
            };
            root.SetPadding(32, 24, 32, 4);

            EditText BuildField(string hint, string value, Android.Text.InputTypes type = Android.Text.InputTypes.ClassText)
            {
                var edit = new EditText(this)
                {
                    Hint = hint,
                    Text = value
                };
                edit.InputType = type;
                root.AddView(edit);
                return edit;
            }

            var ownerInput = BuildField("GitHub owner", settings.Owner);
            var repoInput = BuildField("GitHub repo", settings.Repo);
            var branchInput = BuildField("Branch", settings.Branch);
            var spaInput = BuildField("SPA base URL", settings.SpaBaseUrl, Android.Text.InputTypes.TextVariationUri);
            var tokenInput = BuildField("GitHub token (repo:contents)", settings.Token, Android.Text.InputTypes.ClassText | Android.Text.InputTypes.TextVariationPassword);

            var dialog = new AlertDialog.Builder(this)
                .SetTitle(GetString(Resource.String.github_db_settings_title))
                .SetView(root)
                .SetNegativeButton(GetString(Android.Resource.String.Cancel), (_, _) => { })
                .SetPositiveButton(GetString(Android.Resource.String.Ok), (_, _) =>
                {
                    SaveDbSettings(new DbSettings(
                        ownerInput.Text ?? DefaultDbOwner,
                        repoInput.Text ?? DefaultDbRepo,
                        branchInput.Text ?? DefaultDbBranch,
                        tokenInput.Text ?? string.Empty,
                        spaInput.Text ?? DefaultSpaBaseUrl));

                    Toast.MakeText(this, Resource.String.github_db_settings_saved, ToastLength.Short)?.Show();
                })
                .Create();

            dialog.Show();
        }

        private async Task<(List<IndexItem> Items, string? Sha)> LoadIndexAsync(DbSettings settings)
        {
            try
            {
                var (content, sha) = await GetRepoFileAsync(settings, "data/index.json");
                if (string.IsNullOrWhiteSpace(content))
                {
                    return ([], sha);
                }

                var list = JsonSerializer.Deserialize<List<IndexItem>>(
                    content,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
                return (list, sha);
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("404", StringComparison.OrdinalIgnoreCase))
            {
                // index.json not created yet - first write flow.
                return ([], null);
            }
        }

        private async Task<(string Content, string? Sha)> GetRepoFileAsync(DbSettings settings, string path)
        {
            using var client = BuildGitHubClient(settings.Token);
            var endpoint = $"https://api.github.com/repos/{settings.Owner}/{settings.Repo}/contents/{path}?ref={settings.Branch}";
            var json = await client.GetStringAsync(endpoint);

            using var doc = JsonDocument.Parse(json);
            var contentB64 = doc.RootElement.GetProperty("content").GetString() ?? string.Empty;
            contentB64 = contentB64.Replace("\n", string.Empty).Replace("\r", string.Empty);
            var bytes = Convert.FromBase64String(contentB64);
            var content = Encoding.UTF8.GetString(bytes);

            var sha = doc.RootElement.TryGetProperty("sha", out var shaEl) ? shaEl.GetString() : null;
            return (content, sha);
        }

        private async Task PutRepoFileAsync(DbSettings settings, string path, string content, string message, string? sha)
        {
            using var client = BuildGitHubClient(settings.Token);
            var endpoint = $"https://api.github.com/repos/{settings.Owner}/{settings.Repo}/contents/{path}";
            var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(content));

            var payload = new Dictionary<string, object?>
            {
                ["message"] = message,
                ["content"] = b64,
                ["branch"] = settings.Branch
            };
            if (!string.IsNullOrWhiteSpace(sha))
            {
                payload["sha"] = sha;
            }

            var body = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var resp = await client.PutAsync(endpoint, body);
            if (!resp.IsSuccessStatusCode)
            {
                var details = await resp.Content.ReadAsStringAsync();
                throw new InvalidOperationException($"{(int)resp.StatusCode}: {details}");
            }
        }

        private static string Slugify(string text)
        {
            var slug = Regex.Replace(text.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
            return string.IsNullOrWhiteSpace(slug) ? "article" : slug[..Math.Min(slug.Length, 48)];
        }

        private sealed record DbSettings(string Owner, string Repo, string Branch, string Token, string SpaBaseUrl);

        private sealed record IndexItem
        {
            public string Id { get; init; } = string.Empty;
            public string Title { get; init; } = string.Empty;
            public string SourceUrl { get; init; } = string.Empty;
            public string SourceDomain { get; init; } = string.Empty;
            public string HeroImageUrl { get; init; } = string.Empty;
            public List<string> Tags { get; init; } = [];
            public string Path { get; init; } = string.Empty;
            public DateTimeOffset SavedAtUtc { get; init; }
        }

        public sealed record StoryRecord
        {
            public string Title { get; init; } = string.Empty;
            public string SourceUrl { get; init; } = string.Empty;
            public string SourceDomain { get; init; } = string.Empty;
            public string HeroImageUrl { get; init; } = string.Empty;
            public List<string> Tags { get; init; } = [];
            public List<ParagraphPair> Paragraphs { get; init; } = [];
            public DateTimeOffset SavedAtUtc { get; init; }
        }

        public sealed record ParagraphPair(string English, string Pidgin);
        private sealed record FeedArticle(string Title, string Url, string Source);
    }
}