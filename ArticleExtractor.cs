using Android.Net;
using Android.Webkit;
using System.Net;
using System.Text.RegularExpressions;
using SystemUri = System.Uri;

namespace wetin_happen_dem_internets
{
    /// <summary>
    /// Two-stage article extractor.
    /// Stage 1 – WebView: handles JS redirects, SPA rendering, lazy content.
    /// Stage 2 – HTTP fallback: used when WebView yields no usable text.
    /// </summary>
    internal sealed class ArticleExtractor : IDisposable
    {
        private readonly Android.App.Activity _activity;
        private WebView? _webView;
        private bool _disposed;

        // ── Separator that is vanishingly unlikely to appear in article text ──────
        private const string Sep = "⁂⁂⁂";

        // ── JS: extracts image + text; returns "IMG_URL{Sep}TITLE{Sep}para1..." ───
        private static readonly string HarvestScript = $$"""
            (function(){
              try {
                var noise = ['script','style','nav','header','footer','aside',
                             'iframe','noscript','form','dialog',
                             '.ad','.ads','.advertisement','.cookie-banner',
                             '.popup','.modal','[role="banner"]',
                             '[role="navigation"]','[role="complementary"]',
                             '[aria-label="Advertisement"]'];
                noise.forEach(function(s){
                  try{ document.querySelectorAll(s).forEach(function(e){e.remove()}); }catch(x){}
                });

                var SEP = '{{Sep}}';

                // ── Hero image ──────────────────────────────────────────────────
                var imgUrl = '';
                var imgMeta = document.querySelector('meta[property="og:image"]')
                           || document.querySelector('meta[name="twitter:image:src"]')
                           || document.querySelector('meta[name="twitter:image"]');
                if(imgMeta) imgUrl = imgMeta.getAttribute('content') || '';
                if(!imgUrl || !imgUrl.startsWith('http')){
                  var firstImg = document.querySelector('article img[src]')
                              || document.querySelector('[class*="article"] img[src]')
                              || document.querySelector('main img[src]');
                  if(firstImg){
                    var src = firstImg.getAttribute('src') || '';
                    if(src.startsWith('http') && !src.includes('logo') && !src.includes('icon') && !src.includes('avatar'))
                      imgUrl = src;
                  }
                }

                // ── Title ───────────────────────────────────────────────────────
                var title = (document.querySelector('h1') || {innerText:''}).innerText.trim()
                         || document.title.split('|')[0].split('-')[0].split('–')[0].trim()
                         || document.title.trim();

                // Strategy A: ld+json articleBody
                var ldNodes = document.querySelectorAll('script[type="application/ld+json"]');
                for(var i=0;i<ldNodes.length;i++){
                  try{
                    var d = JSON.parse(ldNodes[i].textContent);
                    var body = (Array.isArray(d)?d[0]:d).articleBody || '';
                    if(body.length > 200){
                      var lines = body.split(/\n+/).filter(function(l){ return l.trim().length > 40; });
                      if(lines.length) return imgUrl + SEP + title + SEP + lines.join(SEP);
                    }
                  }catch(x){}
                }

                // Strategy B: semantic article tag
                var artEl = document.querySelector('article');
                if(artEl && artEl.innerText.trim().length > 200){
                  var ps = Array.from(artEl.querySelectorAll('p'))
                                .map(function(p){ return p.innerText.trim(); })
                                .filter(function(t){ return t.length > 40; });
                  if(ps.length >= 2) return imgUrl + SEP + title + SEP + ps.join(SEP);
                  var lines = artEl.innerText.trim().split(/\n+/)
                               .filter(function(l){ return l.trim().length > 40; });
                  if(lines.length) return imgUrl + SEP + title + SEP + lines.join(SEP);
                }

                // Strategy C: known article-body selectors
                var sels = [
                  '[itemprop="articleBody"]','[class*="article-body"]',
                  '[class*="article__body"]','[class*="story-body"]',
                  '[class*="post-content"]','[class*="entry-content"]',
                  '[class*="content-body"]','[class*="article-content"]',
                  '[class*="story-content"]','[class*="post-body"]',
                  '[data-module="article-body"]','#article-body','.article','main'
                ];
                for(var j=0;j<sels.length;j++){
                  var el = document.querySelector(sels[j]);
                  if(el && el.innerText.trim().length > 200){
                    var ps2 = Array.from(el.querySelectorAll('p'))
                                   .map(function(p){ return p.innerText.trim(); })
                                   .filter(function(t){ return t.length > 40; });
                    if(ps2.length >= 2) return imgUrl + SEP + title + SEP + ps2.join(SEP);
                    var lines2 = el.innerText.trim().split(/\n+/)
                                  .filter(function(l){ return l.trim().length > 40; });
                    if(lines2.length) return imgUrl + SEP + title + SEP + lines2.join(SEP);
                  }
                }

                // Strategy D: densest <p> cluster
                var allP = Array.from(document.querySelectorAll('p'))
                               .map(function(p){ return p.innerText.trim(); })
                               .filter(function(t){ return t.length > 50; });
                if(allP.length >= 3) return imgUrl + SEP + title + SEP + allP.join(SEP);

                // Strategy E: body text split by newlines
                var bodyText = document.body.innerText.trim();
                if(bodyText.length > 400){
                  var bodyLines = bodyText.split(/\n+/)
                                         .filter(function(l){ return l.trim().length > 50; });
                  if(bodyLines.length >= 2) return imgUrl + SEP + title + SEP + bodyLines.join(SEP);
                }

                return 'FAIL';
              } catch(e) { return 'ERROR:' + e.message; }
            })()
            """;

        public ArticleExtractor(Android.App.Activity activity)
        {
            _activity = activity;
        }

        // ── Public API ────────────────────────────────────────────────────────────

        public async Task<ExtractedArticle?> ExtractAsync(SystemUri url, CancellationToken ct = default)
        {
            // Stage 1: WebView
            var webResult = await ExtractViaWebViewAsync(url, ct);
            if (webResult is not null && webResult.Paragraphs.Count > 0)
            {
                return webResult;
            }

            // Stage 2: HTTP fallback (handles paywalls, blocking, simple HTML pages)
            ct.ThrowIfCancellationRequested();
            return await ExtractViaHttpAsync(url, ct);
        }

        // ── Stage 1: WebView ──────────────────────────────────────────────────────

        private Task<ExtractedArticle?> ExtractViaWebViewAsync(SystemUri url, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<ExtractedArticle?>(TaskCreationOptions.RunContinuationsAsynchronously);
            ct.Register(() => tcs.TrySetResult(null));

            _activity.RunOnUiThread(() =>
            {
                try
                {
                    _webView = new WebView(_activity);
                    _webView.Settings.JavaScriptEnabled = true;
                    _webView.Settings.DomStorageEnabled = true;
                    _webView.Settings.LoadsImagesAutomatically = false;
                    _webView.Settings.BlockNetworkImage = true;
                    _webView.Settings.MediaPlaybackRequiresUserGesture = true;
                    _webView.Settings.UserAgentString =
                        "Mozilla/5.0 (Linux; Android 14; Pixel 8 Pro) " +
                        "AppleWebKit/537.36 (KHTML, like Gecko) " +
                        "Chrome/124.0.6367.82 Mobile Safari/537.36";

                    _webView.SetWebViewClient(new PollingWebViewClient(_activity, tcs, HarvestScript, Sep));
                    _webView.LoadUrl(url.ToString());

                    // Absolute ceiling
                    _ = Task.Delay(22_000, ct).ContinueWith(_ =>
                    {
                        if (!tcs.Task.IsCompleted)
                        {
                            tcs.TrySetResult(null);
                            _activity.RunOnUiThread(() => _webView?.StopLoading());
                        }
                    }, TaskScheduler.Default);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });

            return tcs.Task;
        }

        // ── Stage 2: HTTP ─────────────────────────────────────────────────────────

        private static async Task<ExtractedArticle?> ExtractViaHttpAsync(SystemUri url, CancellationToken ct)
        {
            try
            {
                using var handler = new HttpClientHandler
                {
                    AllowAutoRedirect = true,
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
                };
                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Mobile Safari/537.36");
                client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml");
                client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");

                var html = await client.GetStringAsync(url, ct);
                return ParseHtml(html, url);
            }
            catch
            {
                return null;
            }
        }

        private static ExtractedArticle? ParseHtml(string html, SystemUri sourceUri)
        {
            var title = ExtractTitle(html, sourceUri);

            // Hero image from og:image
            var ogImgMatch = Regex.Match(html,
                @"<meta[^>]+property=""og:image""[^>]+content=""([^""]+)""",
                RegexOptions.IgnoreCase);
            var imageUrl = ogImgMatch.Success ? WebUtility.HtmlDecode(ogImgMatch.Groups[1].Value.Trim()) : null;
            if (string.IsNullOrWhiteSpace(imageUrl) || !imageUrl.StartsWith("http"))
                imageUrl = null;

            var paragraphs = new List<string>();

            // Try ld+json first
            var ldMatch = Regex.Match(html, @"""articleBody""\s*:\s*""((?:[^""\\]|\\.)*)""", RegexOptions.Singleline);
            if (ldMatch.Success)
            {
                var body = Regex.Unescape(ldMatch.Groups[1].Value);
                paragraphs.AddRange(SplitText(body));
            }

            // <p> tags
            if (paragraphs.Count < 3)
            {
                var pMatches = Regex.Matches(html, @"<p[^>]*>(.*?)</p>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                foreach (Match m in pMatches)
                {
                    var text = CleanHtml(m.Groups[1].Value);
                    if (text.Length > 50) paragraphs.Add(text);
                }
            }

            // meta description
            if (paragraphs.Count == 0)
            {
                var metaMatch = Regex.Match(html,
                    @"<meta[^>]+(?:name=""description""|property=""og:description"")[^>]*content=""([^""]+)""",
                    RegexOptions.IgnoreCase);
                if (metaMatch.Success)
                {
                    var desc = CleanHtml(metaMatch.Groups[1].Value);
                    if (desc.Length > 30) paragraphs.Add(desc);
                }
            }

            var distinct = paragraphs.Distinct().Take(25).ToList();
            return distinct.Count == 0 ? null : new ExtractedArticle(title, distinct, imageUrl);
        }

        private static string ExtractTitle(string html, SystemUri fallback)
        {
            // Try og:title first (cleaner than <title>)
            var og = Regex.Match(html, @"<meta[^>]+property=""og:title""[^>]+content=""([^""]+)""", RegexOptions.IgnoreCase);
            if (og.Success) return WebUtility.HtmlDecode(og.Groups[1].Value.Trim());

            var t = Regex.Match(html, @"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (t.Success) return WebUtility.HtmlDecode(CleanHtml(t.Groups[1].Value));

            return fallback.Host;
        }

        private static List<string> SplitText(string text) =>
            text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => Regex.Replace(l.Trim(), @"\s+", " "))
                .Where(l => l.Length > 50)
                .Distinct()
                .ToList();

        private static string CleanHtml(string html)
        {
            var noTags = Regex.Replace(html, @"<[^>]+>", " ", RegexOptions.Singleline);
            return Regex.Replace(WebUtility.HtmlDecode(noTags), @"\s+", " ").Trim();
        }

        // ── WebViewClient with adaptive polling ───────────────────────────────────

        private sealed class PollingWebViewClient : WebViewClient
        {
            private readonly Android.App.Activity _activity;
            private readonly TaskCompletionSource<ExtractedArticle?> _tcs;
            private readonly string _script;
            private readonly string _sep;
            private bool _settled;
            private int _pollCount;
            private const int MaxPolls = 6;
            private const int PollIntervalMs = 900;

            public PollingWebViewClient(
                Android.App.Activity activity,
                TaskCompletionSource<ExtractedArticle?> tcs,
                string script,
                string sep)
            {
                _activity = activity;
                _tcs = tcs;
                _script = script;
                _sep = sep;
            }

            public override void OnPageFinished(WebView? view, string? url)
            {
                if (_settled || view is null) return;
                SchedulePoll(view, PollIntervalMs);
            }

            public override void OnReceivedError(WebView? view, IWebResourceRequest? request, WebResourceError? error)
            {
                if (request?.IsForMainFrame == true && !_settled)
                {
                    _settled = true;
                    _tcs.TrySetResult(null);
                }
            }

            private void SchedulePoll(WebView view, int delayMs)
            {
                _ = Task.Delay(delayMs).ContinueWith(_ =>
                {
                    if (_settled) return;
                    _activity.RunOnUiThread(() => view.EvaluateJavascript(_script, new JsCallback(raw =>
                    {
                        if (_settled) return;

                        var result = ParseRaw(raw);
                        if (result is not null && result.Paragraphs.Count > 0)
                        {
                            _settled = true;
                            _tcs.TrySetResult(result);
                            return;
                        }

                        _pollCount++;
                        if (_pollCount >= MaxPolls)
                        {
                            _settled = true;
                            _tcs.TrySetResult(null);
                        }
                        else
                        {
                            // Back-off: each successive poll waits longer
                            SchedulePoll(view, PollIntervalMs + _pollCount * 400);
                        }
                    })));
                }, TaskScheduler.Default);
            }

            private ExtractedArticle? ParseRaw(string? raw)
            {
                if (string.IsNullOrWhiteSpace(raw) || raw == "null") return null;

                var text = raw.Trim();
                if (text.StartsWith('"') && text.EndsWith('"'))
                {
                    text = text[1..^1]
                        .Replace("\\n", "\n")
                        .Replace("\\t", " ")
                        .Replace("\\\"", "\"")
                        .Replace("\\\\", "\\");
                }

                if (text.StartsWith("FAIL") || text.StartsWith("ERROR")) return null;

                var parts = text.Split(_sep, StringSplitOptions.RemoveEmptyEntries);
                // Format: imgUrl ⁂⁂⁂ title ⁂⁂⁂ para1 ⁂⁂⁂ para2 ...
                if (parts.Length < 3) return null;

                var imageUrl = parts[0].Trim().StartsWith("http") ? parts[0].Trim() : null;
                var title = parts[1].Trim();
                var paragraphs = parts[2..]
                    .Select(p => Regex.Replace(p.Trim(), @"\s+", " "))
                    .Where(p => p.Length > 40)
                    .Distinct()
                    .Take(25)
                    .ToList();

                return paragraphs.Count == 0 ? null : new ExtractedArticle(title, paragraphs, imageUrl);
            }
        }

        // ── JS value callback shim ────────────────────────────────────────────────

        private sealed class JsCallback : Java.Lang.Object, IValueCallback
        {
            private readonly Action<string?> _callback;
            public JsCallback(Action<string?> callback) => _callback = callback;
            public void OnReceiveValue(Java.Lang.Object? value) => _callback(value?.ToString());
        }

        // ── Result type ───────────────────────────────────────────────────────────

        public sealed record ExtractedArticle(string Title, List<string> Paragraphs, string? ImageUrl = null);

        // ── Disposal ──────────────────────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _activity.RunOnUiThread(() =>
            {
                _webView?.StopLoading();
                _webView?.Destroy();
                _webView?.Dispose();
                _webView = null;
            });
        }
    }
}
