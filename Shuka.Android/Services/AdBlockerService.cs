namespace Shuka.Android.Services;

/// <summary>
/// Ad blocker with two layers, both driven from injected JavaScript:
///   1. Network: monkey-patches fetch/XHR/createElement and calls back
///      to C# via a JavascriptInterface bridge (ShukaAdBlock.shouldBlock)
///      to decide whether each URL should be blocked.
///   2. Cosmetic: injects CSS hiding rules + DOM removal + MutationObserver
///      for dynamically injected ad elements.
///
/// AdBlockingWebViewHandler wraps MAUI’s WebViewClient and uses
/// ShouldInterceptRequest plus a @JavascriptInterface (ShouldBlock) for scripts.
///
/// Filter list (EasyList China + AdGuard Chinese) is downloaded once and
/// cached to a local file — avoids the Android Preferences 512KB limit.
///
/// Same-origin “bootstrap” ad scripts (e.g. 52shuku ad_top.js) are blocked via
/// URL/path rules and native ShouldInterceptRequest — never block the entire
/// reader domain or the main HTML document fails to load.
/// </summary>
public class AdBlockerService
{
    // ── Built-in domains (always active) ─────────────────────────────────────
    // Declared BEFORE Instance so they're initialized before the constructor runs.
    private static readonly string[] BuiltinDomains =
    {
        // Chinese ad networks (primary targets for Chinese novel sites)
        "pubfuture.com", "cdn.pubfuture.com", "js.pubfuture.com",
        "pubfuture-ad.com",
        "s.click.aliexpress.com", "click.aliexpress.com",
        "tanx.com", "alimama.com", "mmstat.com", "cnzz.com",
        "pos.baidu.com", "hm.baidu.com", "union.baidu.com", "cpro.baidu.com",
        "dup.baidustatic.com", "ubmcmm.baidustatic.com",
        "taobao.com", "tbcdn.cn", "alicdn.com",
        "sogou.com", "go.sohu.com", "beacon.sina.com.cn",
        "irs01.com", "irs01.net", "irs09.com",
        "mediav.com", "admaster.com.cn", "ipinyou.com",
        "gridsumdissector.com", "miaozhen.com",
        
        // Global ad networks
        "doubleclick.net", "googlesyndication.com", "googleadservices.com",
        "google-analytics.com", "googletagmanager.com", "googletagservices.com",
        "adservice.google.com", "pagead2.googlesyndication.com",
        "taboola.com", "outbrain.com", "criteo.com", "pubmatic.com",
        "advertising.com", "adnxs.com", "rubiconproject.com", "openx.net",
        "adsrvr.org", "adform.net", "serving-sys.com", "2mdn.net",
        "moatads.com", "adsafeprotected.com", "amazon-adsystem.com",
        "media.net", "mgid.com", "revcontent.com", "sharethrough.com",
        "triplelift.com", "indexexchange.com", "appnexus.com",
        "sovrn.com", "lijit.com", "33across.com", "smartadserver.com",
        "yieldmo.com", "undertone.com", "conversantmedia.com", "adroll.com",
        "bidswitch.net", "casalemedia.com", "contextweb.com",
        "emxdgt.com", "lkqd.net", "rhythmone.com", "spotxchange.com",
        "teads.tv", "yieldlab.net",
        
        // Analytics & tracking
        "scorecardresearch.com", "quantserve.com", "chartbeat.com",
        "newrelic.com", "nr-data.net", "hotjar.com",
        
        // Social media trackers
        "facebook.net", "connect.facebook.net", "pixel.facebook.com",
        "twitter.com", "platform.twitter.com", "analytics.twitter.com",
    };

    // ── Built-in cosmetic selectors (always active) ───────────────────────────
    // Comprehensive list targeting common ad container patterns
    private static readonly string[] BuiltinCosmetic =
    {
        // 52shuku.net / PubFuture (ad_top.js injects pf-* units)
        ".advertisements-link", ".SecondaryAd", ".adroot", ".WPBannerizeWidget", ".iframead",
        ".ad_slot", "#goog_plcm_frame",
        "[id^='pf-']", "[data-format='display'][data-lazy='true']",
        
        // Generic ad containers (class-based)
        "[class*='ad-wrap']", "[class*='ad-box']", "[class*='ad-area']",
        "[class*='ad-slot']", "[class*='ad-unit']", "[class*='ad-banner']",
        "[class*='ad-container']", "[class*='ad-block']", "[class*='ad-content']",
        "[class*='ad-top']", "[class*='ad-bottom']", "[class*='ad-side']",
        "[class*='ad-left']", "[class*='ad-right']", "[class*='ad-center']",
        "[class*='ad-float']", "[class*='ad-fixed']", "[class*='ad-sticky']",
        "[class*='ad-popup']", "[class*='ad-overlay']", "[class*='ad-modal']",
        "[class*='ad-widget']", "[class*='ad-frame']", "[class*='ad-holder']",
        "[class*='ad-space']", "[class*='ad-zone']", "[class*='ad-native']",
        "[class*='ad-text']", "[class*='ad-image']", "[class*='ad-video']",
        "[class*='ad_']", "[class*='_ad']", "[class*='-ad-']",
        "[class*='ads-']", "[class*='-ads']", "[class*='_ads']",
        "[class*='advertisement']", "[class*='advert']",
        
        // Generic ad containers (id-based)
        "[id*='ad-wrap']", "[id*='ad-box']", "[id*='ad-area']",
        "[id*='ad-slot']", "[id*='ad-unit']", "[id*='ad-banner']",
        "[id*='ad-container']", "[id*='ad-block']", "[id*='ad-content']",
        "[id*='ad-top']", "[id*='ad-bottom']", "[id*='ad-side']",
        "[id*='ad-left']", "[id*='ad-right']", "[id*='ad-center']",
        "[id*='ad-float']", "[id*='ad-fixed']", "[id*='ad-sticky']",
        "[id*='ad-popup']", "[id*='ad-overlay']", "[id*='ad-modal']",
        "[id*='ad-widget']", "[id*='ad-frame']", "[id*='ad-holder']",
        "[id*='ad-space']", "[id*='ad-zone']",
        "[id*='ad_']", "[id*='_ad']", "[id*='-ad-']",
        "[id*='ads-']", "[id*='-ads']", "[id*='_ads']",
        
        // Advertisement variations
        "[class*='advertisement']", "[class*='advert']", "[class*='ads-']",
        "[id*='advertisement']", "[id*='advert']", "[id*='ads-']",
        ".ad", "#ad", ".ads", "#ads",
        
        // Sponsored content
        "[class*='sponsored']", "[class*='sponsor']",
        "[id*='sponsored']", "[id*='sponsor']",
        
        // Specific ad networks
        "[class*='pubfuture']", "[id*='pubfuture']",
        "[class*='taboola']", "[id*='taboola']",
        "[class*='outbrain']", "[id*='outbrain']",
        "[class*='criteo']", "[id*='criteo']",
        "[class*='mgid']", "[id*='mgid']",
        "[class*='adsense']", "[id*='adsense']",
        "[class*='adroll']", "[id*='adroll']",
        "[class*='doubleclick']", "[id*='doubleclick']",
        
        // Google Ads
        "ins.adsbygoogle", "[id*='google_ads']", "[id*='div-gpt-ad']",
        "[class*='google-ad']", "[id*='google-ad']",
        
        // Iframes (common ad delivery method)
        "iframe[src*='pubfuture']", "iframe[src*='doubleclick']",
        "iframe[src*='googlesyndication']", "iframe[src*='taboola']",
        "iframe[src*='outbrain']", "iframe[src*='criteo']",
        "iframe[src*='/ad']", "iframe[src*='/ads/']",
        "iframe[src*='banner']", "iframe[src*='popup']",
        
        // Links to ad networks
        "a[href*='pubfuture.com']", "a[href*='s.click.aliexpress.com']",
        "a[href*='doubleclick.net']", "a[href*='googlesyndication.com']",
        
        // Images from ad networks
        "img[src*='pubfuture']", "img[src*='doubleclick']",
        "img[src*='/ad.']", "img[src*='/ads/']",
        "img[src*='banner']",
        
        // Chinese novel site specific patterns
        "[class*='guanggao']", "[id*='guanggao']", // 广告 (advertisement in Chinese)
        "[class*='tuiguang']", "[id*='tuiguang']", // 推广 (promotion in Chinese)
        "[class*='zanzhu']", "[id*='zanzhu']", // 赞助 (sponsored in Chinese)
        
        // Common banner/popup patterns
        "[class*='banner']", "[id*='banner']",
        "[class*='popup']", "[id*='popup']",
        "[class*='modal-ad']", "[id*='modal-ad']",
        "[class*='overlay-ad']", "[id*='overlay-ad']",
        
        // Floating/sticky ads
        "[style*='position: fixed']iframe",
        "[style*='position:fixed']iframe",
        "div[style*='position: fixed'][style*='z-index']",
        "div[style*='position:fixed'][style*='z-index']",
    };

    // ── Constants and singleton ───────────────────────────────────────────────
    // Declared AFTER the built-in arrays so they're ready when Instance is created.
    private const string PrefKeyEnabled   = "adblocker_enabled";
    private const string PrefKeyListDate  = "adblocker_list_date";
    private const int    CacheHours       = 24;
    private const int    MaxCosmeticRules = 80;

    private static readonly string[] FilterListUrls =
    {
        "https://filters.adtidy.org/extension/ublock/filters/224.txt",
        "https://easylist-downloads.adblockplus.org/easylistchina.txt",
    };

    // Instance is created AFTER all static fields above are initialized.
    public static readonly AdBlockerService Instance = new();

    public bool IsEnabled
    {
        get => Preferences.Default.Get(PrefKeyEnabled, true);
        set => Preferences.Default.Set(PrefKeyEnabled, value);
    }

    private readonly HashSet<string> _blockedDomains;
    private readonly HashSet<string> _cosmeticSelectors;

    private bool _initialized;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private AdBlockerService()
    {
        // Initialize collections with built-ins SYNCHRONOUSLY so they're
        // available immediately — before the async filter-list download finishes.
        _blockedDomains    = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _cosmeticSelectors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var d in BuiltinDomains)
                if (!string.IsNullOrEmpty(d)) _blockedDomains.Add(d);

            foreach (var s in BuiltinCosmetic)
                if (!string.IsNullOrEmpty(s)) _cosmeticSelectors.Add(s);

            System.Diagnostics.Debug.WriteLine(
                $"[AdBlocker] Initialized with {_blockedDomains.Count} domains, " +
                $"{_cosmeticSelectors.Count} selectors");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AdBlocker] Constructor error: {ex.Message}");
        }

        // Kick off async download of extended filter lists in the background.
        // Built-ins above are already active; the download just adds more rules.
        _ = InitializeAsync();
    }
    
    // ── Network blocking ──────────────────────────────────────────────────────

    public bool ShouldBlock(string url)
    {
        if (!IsEnabled) return false;
        if (string.IsNullOrWhiteSpace(url)) return false;

        try
        {
            var lowerUrl = url.ToLowerInvariant();

            // Same-origin bootstrap scripts that only load third-party ads (52shuku etc.)
            if (lowerUrl.Contains("ad_top.js") || lowerUrl.Contains("ad_bottom.js") ||
                lowerUrl.Contains("ad_head.js") || lowerUrl.Contains("ad_footer.js"))
            {
                System.Diagnostics.Debug.WriteLine($"[AdBlocker] ✓ BLOCKED (novel ad bootstrap): {url}");
                return true;
            }

            // Pattern-based blocking
            if (lowerUrl.Contains("/ad.") || lowerUrl.Contains("/ads/") ||
                lowerUrl.Contains("/adv/") || lowerUrl.Contains("/banner") ||
                lowerUrl.Contains("/popup") || lowerUrl.Contains("_ad.") ||
                lowerUrl.Contains("_ads.") || lowerUrl.Contains("-ad.") ||
                lowerUrl.Contains("-ads.") || lowerUrl.Contains("adserver") ||
                lowerUrl.Contains("adservice") || lowerUrl.Contains("adsystem") ||
                lowerUrl.Contains("adtech") || lowerUrl.Contains("adview") ||
                lowerUrl.Contains("advertising") || lowerUrl.Contains("pagead") ||
                lowerUrl.Contains("sponsored") || lowerUrl.Contains("/track/") ||
                lowerUrl.Contains("/tracking/") || lowerUrl.Contains("/analytics/") ||
                lowerUrl.Contains("/beacon/") || lowerUrl.Contains("/pixel") ||
                lowerUrl.Contains("/impression"))
            {
                System.Diagnostics.Debug.WriteLine($"[AdBlocker] ✓ BLOCKED (pattern): {url}");
                return true;
            }

            // Domain-based blocking
            var host = new Uri(url).Host.ToLowerInvariant();
            var parts = host.Split('.');
            for (int i = 0; i < parts.Length - 1; i++)
            {
                var domain = string.Join(".", parts.Skip(i));
                if (_blockedDomains.Contains(domain))
                {
                    System.Diagnostics.Debug.WriteLine($"[AdBlocker] ✓ BLOCKED (domain {domain}): {url}");
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AdBlocker] ShouldBlock error: {ex.Message}");
            return false;
        }
    }

    // ── Cosmetic filter JS ────────────────────────────────────────────────────

    /// <summary>
    /// Returns an aggressive ad blocking script with TWO layers:
    ///   Layer 1 – NETWORK: monkey-patches fetch, XMLHttpRequest.open, and
    ///             document.createElement to call the native ShukaAdBlock
    ///             JavascriptInterface bridge for URL-level blocking.
    ///   Layer 2 – COSMETIC: CSS hiding + DOM removal + MutationObserver
    ///             (same as before).
    ///
    /// The ShukaAdBlock bridge is installed by AdBlockingWebViewHandler and
    /// exposes shouldBlock(url), isEnabled(), and log(msg).
    /// </summary>
    public string GetCosmeticFilterScript()
    {
        System.Diagnostics.Debug.WriteLine(
            $"[AdBlocker] GetCosmeticFilterScript - IsEnabled:{IsEnabled}, Selectors:{_cosmeticSelectors.Count}");

        if (!IsEnabled) return string.Empty;
        if (_cosmeticSelectors.Count == 0)
        {
            System.Diagnostics.Debug.WriteLine("[AdBlocker] ⚠ No cosmetic selectors!");
            return string.Empty;
        }

        // Build the CSS rule for hiding
        var selectors = string.Join(",", _cosmeticSelectors);
        var css = $"{selectors}{{display:none!important;visibility:hidden!important;" +
                  "height:0!important;min-height:0!important;max-height:0!important;" +
                  "width:0!important;min-width:0!important;max-width:0!important;" +
                  "overflow:hidden!important;opacity:0!important;" +
                  "pointer-events:none!important;position:absolute!important;" +
                  "left:-9999px!important;top:-9999px!important}}";

        // Base64-encode to avoid quoting issues
        var cssB64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(css));

        return $$"""
(function(){
  if(window.__shuka_ab)return;
  window.__shuka_ab=true;

  // Helper: check bridge availability
  var hasBridge=typeof ShukaAdBlock!=='undefined';
  function slog(m){ try{ if(hasBridge)ShukaAdBlock.log(m);else console.log('[Shuka]',m); }catch(e){ } }
  function askBlock(url){
    try{if(hasBridge)return ShukaAdBlock.shouldBlock(url)==='true';}catch(e){}
    return false;
  }
  slog('🛡️ AD BLOCKER ACTIVATED (bridge='+(hasBridge?'YES':'NO')+')');

  // ── LAYER 1: NETWORK BLOCKING via JS bridge ─────────────────────────────

  // 1a. Monkey-patch fetch()
  if(hasBridge&&window.fetch){
    var _origFetch=window.fetch;
    window.fetch=function(input,init){
      var url=(typeof input==='string')?input:(input&&input.url?input.url:'');
      if(url&&askBlock(url)){
        slog('🚫 fetch blocked: '+url);
        return Promise.reject(new TypeError('Blocked by Shuka AdBlocker'));
      }
      return _origFetch.apply(this,arguments);
    };
    slog('✓ fetch() patched');
  }

  // 1b. Monkey-patch XMLHttpRequest.open
  if(hasBridge&&window.XMLHttpRequest){
    var _origOpen=XMLHttpRequest.prototype.open;
    XMLHttpRequest.prototype.open=function(method,url){
      if(url&&askBlock(url)){
        slog('🚫 XHR blocked: '+url);
        this.__shuka_blocked=true;
      }
      return _origOpen.apply(this,arguments);
    };
    var _origSend=XMLHttpRequest.prototype.send;
    XMLHttpRequest.prototype.send=function(){
      if(this.__shuka_blocked){
        // Fire error so callers don't hang
        try{
          Object.defineProperty(this,'status',{get:function(){ return 0; } });
          Object.defineProperty(this,'readyState',{get:function(){ return 4; } });
          if(typeof this.onerror==='function')this.onerror(new Event('error'));
        }catch(e){}
        return;
      }
      return _origSend.apply(this,arguments);
    };
    slog('✓ XMLHttpRequest patched');
  }

  // 1c. Monkey-patch document.createElement to intercept script/iframe/img creation
  if(hasBridge){
    var _origCreate=document.createElement.bind(document);
    document.createElement=function(tag){
      var el=_origCreate(tag);
      var tagLower=(tag||'').toLowerCase();
      if(tagLower==='script'||tagLower==='iframe'||tagLower==='img'){
        // Watch for src being set and block if needed
        var _origSrc=Object.getOwnPropertyDescriptor(HTMLElement.prototype,'src')||
                     Object.getOwnPropertyDescriptor(el.__proto__,'src');
        // Use a setter proxy
        var blocked=false;
        var realSrc='';
        try{
          Object.defineProperty(el,'__shuka_src',{value:'',writable:true});
          var proto=el.__proto__;
          var srcDesc=null;
          while(proto&&!srcDesc){
            srcDesc=Object.getOwnPropertyDescriptor(proto,'src');
            proto=proto.__proto__;
          }
          if(srcDesc&&srcDesc.set){
            var origSet=srcDesc.set;
            var origGet=srcDesc.get;
            Object.defineProperty(el,'src',{
              get:function(){return origGet?origGet.call(this):this.__shuka_src;},
              set:function(v){
                if(v&&askBlock(v)){
                  slog('🚫 createElement.src blocked ('+tagLower+'): '+v);
                  return;
                }
                if(origSet)origSet.call(this,v);
                else this.__shuka_src=v;
              },
              configurable:true
            });
          }
        }catch(e){/* src interception not critical */}
      }
      return el;
    };
    slog('✓ createElement patched');
  }

  // ── LAYER 2: COSMETIC BLOCKING ──────────────────────────────────────────

  // 2a. Inject CSS to hide ads
  try{
    var css=atob('{{cssB64}}');
    var st=_origCreate?_origCreate('style'):document.createElement('style');
    st.id='__shuka_ab_style';
    st.textContent=css;
    (document.head||document.documentElement).appendChild(st);
    slog('✓ CSS injected');
  }catch(e){slog('CSS error: '+e);}

  // 2b. NUCLEAR REMOVAL
  function nukeAds(){
    var removed=0;
    try{
      // Remove suspicious iframes
      document.querySelectorAll('iframe').forEach(function(iframe){
        var src=(iframe.src||'').toLowerCase();
        var id=(iframe.id||'').toLowerCase();
        var cls=(iframe.className||'').toLowerCase();
        if(src.includes('ad')||src.includes('banner')||src.includes('popup')||
           src.includes('casino')||src.includes('play')||src.includes('win')||
           src.includes('video')||src.includes('promo')||src.includes('doubleclick')||
           src.includes('googlesyndication')||src.includes('google-analytics')||
           id.includes('ad')||id.includes('goog')||cls.includes('ad')||
           iframe.offsetWidth===300||iframe.offsetWidth===728||iframe.offsetWidth===160||
           iframe.offsetHeight===250||iframe.offsetHeight===90||iframe.offsetHeight===600){
          if(iframe.parentNode){iframe.parentNode.removeChild(iframe);removed++;}
        }
        // Also check via bridge
        if(hasBridge&&iframe.src&&askBlock(iframe.src)){
          if(iframe.parentNode){iframe.parentNode.removeChild(iframe);removed++;}
        }
      });

      // Remove elements by common ad sizes
      document.querySelectorAll('div,ins,aside,section').forEach(function(el){
        var w=el.offsetWidth,h=el.offsetHeight;
        if((w===300&&h===250)||(w===728&&h===90)||(w===160&&h===600)||
           (w===336&&h===280)||(w===970&&h===90)||(w===320&&h===50)||
           (w===320&&h===100)||(w===468&&h===60)||(w===234&&h===60)){
          if(el.parentNode){el.parentNode.removeChild(el);removed++;}
        }
      });

      // Remove by selector patterns
      var sels='{{string.Join(",", _cosmeticSelectors.Take(50))}},.advertisements-link,.SecondaryAd,.adroot,.WPBannerizeWidget,.iframead,.ad_slot,.ad-total,.m-header-ad,.header_ad_div,.horizontaltextadbox';
      document.querySelectorAll(sels).forEach(function(el){
        if(el&&el.parentNode){el.parentNode.removeChild(el);removed++;}
      });

      // Remove scripts from ad networks (DOM + bridge check)
      document.querySelectorAll('script[src]').forEach(function(script){
        var src=(script.src||'').toLowerCase();
        var shouldKill=src.includes('ad')||src.includes('banner')||src.includes('popup')||
           src.includes('doubleclick')||src.includes('googlesyndication')||
           src.includes('pubfuture')||src.includes('taboola')||src.includes('outbrain')||
           src.includes('casino')||src.includes('promo');
        if(!shouldKill&&hasBridge&&script.src)shouldKill=askBlock(script.src);
        if(shouldKill&&script.parentNode){script.parentNode.removeChild(script);removed++;}
      });

      // Remove elements with ad-like text
      document.querySelectorAll('div,span,a,button').forEach(function(el){
        var text=(el.textContent||'').trim().toLowerCase();
        if(text==='广告'||text==='推广'||text==='赞助'||
           text==='advertisement'||text==='sponsored'||
           text.includes('casino')||text.includes('play now')||
           text.includes('win bigger')||text.includes('play video')){
          var parent=el.parentNode;
          if(parent&&parent.tagName!=='BODY'&&parent.parentNode){
            parent.parentNode.removeChild(parent);removed++;
          }
        }
      });

      // Remove ad-linking anchors
      document.querySelectorAll('a[href]').forEach(function(a){
        var href=(a.href||'').toLowerCase();
        var isAd=href.includes('casino')||href.includes('betting')||href.includes('promo');
        if(!isAd&&hasBridge&&a.href)isAd=askBlock(a.href);
        if(isAd){
          var container=a.closest('div,section,aside');
          if(container&&container.parentNode){container.parentNode.removeChild(container);removed++;}
        }
      });

      // Check all img src via bridge
      if(hasBridge){
        document.querySelectorAll('img[src]').forEach(function(img){
          if(img.src&&askBlock(img.src)){
            if(img.parentNode){img.parentNode.removeChild(img);removed++;}
          }
        });
      }

      if(removed>0)slog('🗑️ Removed '+removed+' ad elements');
    }catch(e){slog('Remove error: '+e);}
  }

  // 2c. Block ad script globals
  try{
    window.googletag=window.googletag||{};
    window.googletag.cmd=window.googletag.cmd||[];
    window.googletag.cmd.push=function(){};
    window.adsbygoogle=window.adsbygoogle||[];
    window.adsbygoogle.push=function(){};
    window.showAd=function(){};
    window.loadAd=function(){};
    window.displayAd=function(){};
    slog('✓ Ad functions blocked');
  }catch(e){slog('Block error: '+e);}

  // 2d. MutationObserver — catches dynamically injected ads
  try{
    var observer=new MutationObserver(function(mutations){
      if(!document.getElementById('__shuka_ab_style')){
        var css=atob('{{cssB64}}');
        var s2=document.createElement('style');
        s2.id='__shuka_ab_style';
        s2.textContent=css;
        (document.head||document.documentElement).appendChild(s2);
      }
      mutations.forEach(function(mutation){
        mutation.addedNodes.forEach(function(node){
          if(node.nodeType!==1)return;
          var src=(node.src||'').toLowerCase();
          var id=(node.id||'').toLowerCase();
          var cls=(node.className||'').toLowerCase();
          var shouldRemove=false;

          // Bridge check first
          if(hasBridge&&node.src&&askBlock(node.src)){shouldRemove=true;}

          if(node.tagName==='IFRAME'){
            if(src.includes('ad')||src.includes('doubleclick')||src.includes('googlesyndication')||
               src.includes('casino')||src.includes('promo')||id.includes('goog')||id.includes('ad'))
              shouldRemove=true;
          }
          if(node.tagName==='SCRIPT'&&src){
            if(src.includes('doubleclick')||src.includes('googlesyndication')||src.includes('googletagservices')||
               src.includes('ad')||src.includes('banner')||src.includes('popup'))
              shouldRemove=true;
          }
          if(id.includes('ad')||cls.includes('ad')||cls.includes('banner')||
             cls.includes('advertisement')||cls.includes('advert')||
             cls.includes('SecondaryAd')||cls.includes('adroot')||cls.includes('iframead'))
            shouldRemove=true;

          if(shouldRemove&&node.parentNode){
            node.parentNode.removeChild(node);
            slog('🚫 Blocked dynamic ad: '+node.tagName+' '+(id||cls));
          }
        });
      });
    });
    observer.observe(document.documentElement,{childList:true,subtree:true});
    slog('✓ Observer active');
  }catch(e){slog('Observer error: '+e);}

  // 2e. Run removal passes
  setInterval(nukeAds,2000);
  nukeAds();
  setTimeout(nukeAds,100);
  setTimeout(nukeAds,500);
  setTimeout(nukeAds,1000);
  setTimeout(nukeAds,2000);
  setTimeout(nukeAds,3000);
  setTimeout(nukeAds,5000);
  setTimeout(nukeAds,8000);
  setTimeout(nukeAds,12000);
  if(document.readyState==='loading'){
    document.addEventListener('DOMContentLoaded',nukeAds);
  }

  slog('🛡️ Ad blocker ready - network+cosmetic active');
})();
""";
    }

    public int BlockedDomainCount  => _blockedDomains.Count;
    public int CosmeticRuleCount   => _cosmeticSelectors.Count;

    public async Task RefreshListAsync()
    {
        _initialized = false;

        try
        {
            Preferences.Default.Remove(PrefKeyListDate);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AdBlocker] Error clearing preferences: {ex.Message}");
        }

        await InitializeAsync();
    }

    // ── Filter list download + parse ──────────────────────────────────────────

    private string CacheFilePath
    {
        get
        {
            try
            {
                return Path.Combine(FileSystem.CacheDirectory, "adblock_list.txt");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AdBlocker] Error getting cache path: {ex.Message}");
                // Fallback to a temp path
                return Path.Combine(Path.GetTempPath(), "shuka_adblock_list.txt");
            }
        }
    }

    private async Task InitializeAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (_initialized) return;

            // Try loading from file cache first
            try
            {
                var lastUpdate = Preferences.Default.Get(PrefKeyListDate, "");
                if (!string.IsNullOrEmpty(lastUpdate) &&
                    DateTime.TryParse(lastUpdate, out var lastDate) &&
                    (DateTime.UtcNow - lastDate).TotalHours < CacheHours &&
                    File.Exists(CacheFilePath))
                {
                    try
                    {
                        var cached = await File.ReadAllTextAsync(CacheFilePath);
                        if (!string.IsNullOrEmpty(cached))
                        {
                            ParseFilterList(cached);
                            _initialized = true;
                            System.Diagnostics.Debug.WriteLine(
                                $"[AdBlocker] Cache loaded: {_blockedDomains.Count} domains, {_cosmeticSelectors.Count} cosmetic");
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[AdBlocker] Cache read error: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AdBlocker] Preferences access error: {ex.Message}");
            }

            await DownloadAndParseAsync();
            _initialized = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AdBlocker] Init error: {ex.Message}");
            _initialized = true; // Mark as initialized even on error to prevent retry loops
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task DownloadAndParseAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");

        foreach (var url in FilterListUrls)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[AdBlocker] Downloading: {url}");
                var content = await http.GetStringAsync(url);

                if (string.IsNullOrEmpty(content) || content.Length < 1000)
                    continue;

                ParseFilterList(content);

                // Save to file cache (no size limit unlike Preferences)
                await File.WriteAllTextAsync(CacheFilePath, content);
                Preferences.Default.Set(PrefKeyListDate, DateTime.UtcNow.ToString("O"));

                System.Diagnostics.Debug.WriteLine(
                    $"[AdBlocker] Downloaded: {_blockedDomains.Count} domains, {_cosmeticSelectors.Count} cosmetic");
                return;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AdBlocker] Download failed ({url}): {ex.Message}");
            }
        }
    }

    private void ParseFilterList(string content)
    {
        // Reset to built-ins
        _blockedDomains.Clear();
        foreach (var d in BuiltinDomains)  _blockedDomains.Add(d);
        _cosmeticSelectors.Clear();
        foreach (var s in BuiltinCosmetic) _cosmeticSelectors.Add(s);

        var newDomains   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var newCosmetic  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 ||
                line[0] == '!' ||
                line[0] == '[' ||
                line.StartsWith("@@") ||
                line[0] == '/')
                continue;

            // Cosmetic: ##selector or domain##selector
            var cosIdx = line.IndexOf("##", StringComparison.Ordinal);
            if (cosIdx >= 0)
            {
                var sel = line[(cosIdx + 2)..].Trim();
                // Skip non-CSS injection rules
                if (sel.Length == 0 || sel[0] == '^' || sel[0] == '#' ||
                    sel[0] == '@' || sel[0] == '+' ||
                    sel.Contains("script:") || sel.Contains("style:") ||
                    sel.Contains(":-abp-") || sel.Contains(":has-text") ||
                    sel.Contains(":matches-css"))
                    continue;

                if (IsValidCssSelector(sel) && newCosmetic.Count < MaxCosmeticRules)
                    newCosmetic.Add(sel);

                continue;
            }

            // Network: ||domain^
            if (line.StartsWith("||"))
            {
                var domain = line[2..];
                var caret  = domain.IndexOf('^');
                if (caret >= 0) domain = domain[..caret];

                if (domain.Contains('/') || domain.Contains('*') ||
                    domain.Contains('?') || domain.Contains('=') ||
                    !domain.Contains('.'))
                    continue;

                newDomains.Add(domain);
            }
        }

        foreach (var d in newDomains)  _blockedDomains.Add(d);
        foreach (var s in newCosmetic) _cosmeticSelectors.Add(s);

        System.Diagnostics.Debug.WriteLine(
            $"[AdBlocker] Parsed +{newDomains.Count} domains, +{newCosmetic.Count} cosmetic rules");
    }

    private static bool IsValidCssSelector(string s) =>
        s.Length > 0 && (s[0] == '.' || s[0] == '#' || s[0] == '[' ||
                         s[0] == '*' || s[0] == ':' || char.IsLetter(s[0]));
}
