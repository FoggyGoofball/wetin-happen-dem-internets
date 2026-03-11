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

        private readonly List<StoryRecord> _savedStories = [];
        private readonly Dictionary<string, StoryRecord> _sessionStoryCache = new(StringComparer.OrdinalIgnoreCase);
        private StoryRecord? _currentStory;
        private readonly List<FeedArticle> _feedArticles = [];

        private const string SavedStoriesKey = "saved_stories";
        private const string DefaultGoogleNewsFeedUrl = "https://news.google.com/rss?hl=en-NG&gl=NG&ceid=NG:en";

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            SetContentView(Resource.Layout.activity_main);

            _urlInput = FindViewById<EditText>(Resource.Id.urlInput);
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

            _fetchButton!.Click += async (_, _) => await FetchAndTranslateAsync();
            _saveButton!.Click  += (_, _) => SaveCurrentStory();
            _shareButton!.Click += (_, _) => ShareCurrentStory();
            _uploadDocsButton!.Click += async (_, _) => await UploadToGoogleDocsAsync();
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

            _feedUrlInput!.Text = DefaultGoogleNewsFeedUrl;

            LoadSavedStoriesFromStorage();
            BindSavedStories();
            BindFeedArticles();
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

                _currentStory = new StoryRecord
                {
                    Title = webArticle.Title,
                    SourceUrl = inputUrl.ToString(),
                    SourceDomain = inputUrl.Host,
                    HeroImageUrl = webArticle.ImageUrl ?? string.Empty,
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
            _citationView!.Text = string.Format(GetString(Resource.String.citation_template), _currentStory.SourceDomain, _currentStory.SourceUrl);

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
            RenderStory();
            SetIdleState(GetString(Resource.String.status_loaded_saved));
        }

        private void ShareCurrentStory()
        {
            if (_currentStory is null)
            {
                Toast.MakeText(this, Resource.String.error_nothing_to_share, ToastLength.Short)?.Show();
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine(_currentStory.Title);
            sb.AppendLine(string.Format(GetString(Resource.String.citation_template), _currentStory.SourceDomain, _currentStory.SourceUrl));
            sb.AppendLine();

            foreach (var p in _currentStory.Paragraphs)
            {
                sb.AppendLine(p.Pidgin);
                sb.AppendLine();
            }

            var sendIntent = new Intent(Intent.ActionSend);
            sendIntent.SetType("text/plain");
            sendIntent.PutExtra(Intent.ExtraText, sb.ToString().TrimEnd());

            StartActivity(Intent.CreateChooser(sendIntent, GetString(Resource.String.share_chooser_title)));
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

        private async Task UploadToGoogleDocsAsync()
        {
            if (_currentStory is null)
            {
                Toast.MakeText(this, Resource.String.upload_docs_nothing, ToastLength.Short)?.Show();
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine(_currentStory.Title);
            sb.AppendLine(new string('─', Math.Min(_currentStory.Title.Length, 60)));
            sb.AppendLine($"Source: {_currentStory.SourceUrl}");
            sb.AppendLine("Translated by: Wetin Happen Dem Internets");
            sb.AppendLine();

            foreach (var p in _currentStory.Paragraphs)
            {
                sb.AppendLine(p.Pidgin);
                sb.AppendLine();
            }

            var intent = new Intent(Intent.ActionSend);
            intent.SetType("text/plain");
            intent.PutExtra(Intent.ExtraTitle, _currentStory.Title);
            intent.PutExtra(Intent.ExtraText, sb.ToString().TrimEnd());

            StartActivity(Intent.CreateChooser(intent, GetString(Resource.String.upload_docs_chooser)));

            await Task.CompletedTask;
        }

        public sealed record StoryRecord
        {
            public string Title { get; init; } = string.Empty;
            public string SourceUrl { get; init; } = string.Empty;
            public string SourceDomain { get; init; } = string.Empty;
            public string HeroImageUrl { get; init; } = string.Empty;
            public List<ParagraphPair> Paragraphs { get; init; } = [];
            public DateTimeOffset SavedAtUtc { get; init; }
        }

        public sealed record ParagraphPair(string English, string Pidgin);
        private sealed record FeedArticle(string Title, string Url, string Source);
    }
}