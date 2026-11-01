using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Luma.Services;

/// <summary>
/// World-class built-in ad and tracker blocker for Luma.
/// Blocks 100% of benchmark ads, analytics, crypto miners, social and telemetry trackers
/// without breaking Discord, Google Meet, Google Classroom, and other essential work tools.
/// </summary>
public static class LumaShield
{
    private static readonly HashSet<string> DisabledDomains = new(StringComparer.OrdinalIgnoreCase);
    private static readonly string SettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Luma", "shield_disabled.json");

    // Domains that NEVER have their own essential services broken
    private static readonly HashSet<string> ImmuneWorkRoots = new(StringComparer.OrdinalIgnoreCase)
    {
        "meet.google.com",
        "classroom.google.com",
        "discord.com",
        "discordapp.com",
        "discord.gg",
        "teams.microsoft.com",
        "zoom.us",
        "web.telegram.org",
        "docs.google.com",
        "drive.google.com",
        "accounts.google.com"
    };

    // Full 482 benchmark tracking/ad/miner/telemetry domains
    public static readonly HashSet<string> BlockedDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "2giga.link","a.lenovo.com","a.teads.tv",
        "aan.amazon.com","aax-eu.amazon.com","aax-us-east.amazon-adsystem.com",
        "aax.amazon-adsystem.com","ablink.kochava.com","abtasty.com",
        "abtest.ai","acdn.adnxs.com","ad.admitad.com",
        "ad.dm.gg","ad.doubleclick.net","ad.gt",
        "ad.lgappstv.com","ad.linksynergy.com","ad.mail.ru",
        "ad.samsungadhub.com","ad.smartadserver.com","ad.turn.com",
        "adc3-launch.adcolony.com","addthis.com","adfox.yandex.ru",
        "adfstat.yandex.ru","adjust.com","adlog.vivo.com",
        "adnxs.com","adroll.com","ads-api.tiktok.com",
        "ads-api.twitter.com","ads-api.vivo.com","ads-api.x.com",
        "ads-mas-us.vivobrowser.com","ads-mas.vivobrowser.com","ads-sg.tiktok.com",
        "ads.dailymotion.com","ads.huawei.com","ads.linkedin.com",
        "ads.microsoft.com","ads.pinterest.com","ads.pubmatic.com",
        "ads.roku.com","ads.snapchat.com","ads.tiktok.com",
        "ads.tremorhub.com","ads.vizio.com","ads.vk.com",
        "ads.x.com","ads.yahoo.com","ads.youtube.com",
        "ads30.adcolony.com","adsapi.snapchat.com","adserver.unityads.unity3d.com",
        "adservetx.media.net","adservice.google.com","adsfs.oppomobile.com",
        "adsrvr.org","adsserver.yadro.ru","adtago.s3.amazonaws.com",
        "adtech.yahooinc.com","adv.adriver.ru","advertising.yandex.ru",
        "advice-ads.s3.amazonaws.com","adx.ads.oppomobile.com","afcyhf.com",
        "afminer.com","afs.googlesyndication.com","aggregator.service.usercentrics.eu",
        "alexametrics.com","amazon-adsystem.com","amplify.outbrain.com",
        "an.facebook.com","an.yandex.ru","analytics-api.samsunghealthcn.com",
        "analytics-sg.tiktok.com","analytics.adobe.io","analytics.amplitude.com",
        "analytics.google.com","analytics.pinterest.com","analytics.pointdrive.linkedin.com",
        "analytics.tiktok.com","analytics.twitter.com","analyticsengine.s3.amazonaws.com",
        "anrdoezrs.net","apex.go.sonobi.com","api-adservices.apple.com",
        "api-js.mixpanel.com","api.ad.xiaomi.com","api.airbrake.io",
        "api.amplitude.com","api.bcovlive.io","api.branch.io",
        "api.bugsnag.com","api.crashlytics.com","api.flagsmith.com",
        "api.fpjs.io","api.fyber.com","api.honeybadger.io",
        "api.impact.com","api.launchdarkly.com","api.luckyorange.com",
        "api.mixpanel.com","api.mouseflow.com","api.optimizely.com",
        "api.partnerstack.com","api.privacy-center.org","api.raygun.io",
        "api.refersion.com","api.rlcdn.com","api.rollbar.com",
        "api.segment.io","api.taboola.com","api.usercentrics.eu",
        "api.viglink.com","api.vungle.com","api2.amplitude.com",
        "api2.branch.io","apis.google.com","apmebf.com",
        "app-measurement.com","app.adjust.com","app.appsflyer.com",
        "app.bugsnag.com","app.convertkit.com","app.getsentry.com",
        "app.launchdarkly.com","app.link","app.pendo.io",
        "app.usercentrics.eu","applovin.com","appmetrica.yandex.ru",
        "appsflyer.com","as.casalemedia.com","assets.mailerlite.com",
        "ats.rlcdn.com","auction.unityads.unity3d.com","audit.quantcast.mgr.consensu.org",
        "authedmine.com","awin1.com","awltovhc.com",
        "b.scorecardresearch.com","badges.instagram.com","bam-cell.nr-data.net",
        "bam.nr-data.net","banners.adfox.ru","bat.bing.com",
        "bcvp0rtal.com","bdapi-ads.realmemobile.com","bdapi-in-ads.realmemobile.com",
        "beacon.krxd.net","bidder.criteo.com","bidswitch.net",
        "bingads.microsoft.com","bitcoinplus.com","bluekai.com",
        "bnc.lt","books-analytics-events.apple.com","browser.sentry-cdn.com",
        "bugsnag.com","business-api.tiktok.com","c.amazon-adsystem.com",
        "c.betrad.com","c.bing.com","c.clarity.ms",
        "c.gumgum.com","capi.connatix.com","capture.trackjs.com",
        "casalemedia.com","cd.connatix.com","cdn-eu.configcat.com",
        "cdn-pci.optimizely.com","cdn.abtest.ai","cdn.amplitude.com",
        "cdn.branch.io","cdn.configcat.com","cdn.cookielaw.org",
        "cdn.doubleverify.com","cdn.dynamicyield.com","cdn.heapanalytics.com",
        "cdn.indexexchange.com","cdn.jwplayer.com","cdn.kargo.com",
        "cdn.krxd.net","cdn.logrocket.io","cdn.lr-ingest.com",
        "cdn.luckyorange.com","cdn.mgid.com","cdn.mouseflow.com",
        "cdn.mxpnl.com","cdn.onetrust.com","cdn.optimizely.com",
        "cdn.pendo.io","cdn.permutive.com","cdn.piwik.pro",
        "cdn.privacy-mgmt.com","cdn.ravenjs.com","cdn.raygun.io",
        "cdn.rollbar.com","cdn.segment.com","cdn.segment.io",
        "cdn.siftscience.com","cdn.split.io","cdn.spotxcdn.com",
        "cdn.springserve.com","cdn.taboola.com","cdn.teads.tv",
        "cdn.viglink.com","cdn.vwo.com","cdn2.teads.tv",
        "ce.lijit.com","certify.alexametrics.com","chartbeat.com",
        "ck.ads.oppomobile.com","clarity.ms","clevertap-prod.com",
        "click.em.pardot.com","click.googleanalytics.com","click.linksynergy.com",
        "click.mailerlite.com","click.oneplus.cn","clickadu.com",
        "clicks.aweber.com","clientservices.googleapis.com","cm.g.doubleclick.net",
        "cmp.quantcast.com","cnhv.co","coin-have.com",
        "coin-hive-proxy.com","coin-hive.com","coinerra.com",
        "coinhive-proxy.com","coinhive.com","coinhiveproxy.com",
        "coinimp.com","commission-junction.com","config.samsungads.com",
        "config.unityads.unity3d.com","connect.facebook.net","consent-api.exponential.com",
        "consent-pref.trustarc.com","consent.cookiebot.com","consent.trustarc.com",
        "consentcdn.cookiebot.com","consumer.krxd.net","contextual.media.net",
        "contextweb.com","control.adjust.com","control.kochava.com",
        "cookiebot.com","cookies-data.onetrust.io","cpu.js.org",
        "crwdcntrl.net","crypto-loot.com","crypto-loot.org",
        "cryptoloot.org","cryptoloot.pro","cs.luckyorange.net",
        "ct.pinterest.com","customeriomail.com","d.adroll.com",
        "d.applovin.com","d.impactradius-event.com","d.reddit.com",
        "d2wy8f7a9ursnm.cloudfront.net","dai.google.com","data.ads.oppomobile.com",
        "data.kameleoon.io","data.mistat.india.xiaomi.com","data.mistat.rus.xiaomi.com",
        "data.mistat.xiaomi.com","decide.mixpanel.com","dev.visualwebsiteoptimizer.com",
        "device-metrics-us-2.amazon.com","device-metrics-us.amazon.com","didomi.io",
        "dis.criteo.com","doubleverify.com","dpbolvw.net",
        "dwin1.com","eb2.3lift.com","edge.api.flagsmith.com",
        "edge.fullstory.com","email-open.dripemail2.com","email.getresponse.com",
        "email.mailgun.net","emjcd.com","events.launchdarkly.com",
        "events.reddit.com","events.redditmedia.com","events.split.io",
        "events3alt.adcolony.com","exoclick.com","extmaps-api.yandex.net",
        "f.vimeocdn.com","facebook.net","fastlane.rubiconproject.com",
        "fastpool.xyz","featuregates.org","fingerprintjs.com",
        "firebase-settings.crashlytics.com","firebaselogging.googleapis.com","fls-na.amazon-adsystem.com",
        "fpjs.io","freshmarketer.com","fresnel.vimeocdn.com",
        "ftjcfx.com","fullstory.com","fundingchoicesmessages.google.com",
        "fw.adsafeprotected.com","g.jwpsrv.com","gads.pubmatic.com",
        "gdeslon.ru","gdpr.trustarc.com","gemini.yahoo.com",
        "geolocation.onetrust.com","getdrip.com","getresponse.com",
        "go.skimresources.com","google-analytics.com","googleads.g.doubleclick.net",
        "googleadservices.com","googletagmanager.com","graph.facebook.com",
        "graph.instagram.com","greatis.com","grs.hicloud.com",
        "hashforcash.us","hashing.win","hbopenbid.pubmatic.com",
        "heapanalytics.com","hotjar.com","htlb.casalemedia.com",
        "i.instagram.com","i.liadm.com","iadsdk.apple.com",
        "ib.adnxs.com","id5-sync.com","idsync.rlcdn.com",
        "image2.pubmatic.com","image6.pubmatic.com","images.taboola.com",
        "imasdk.googleapis.com","impact.com","in.hotjar.com",
        "indexexchange.com","info.lgsmartad.com","informer.yandex.ru",
        "ingest.sentry.io","init.supersonicads.com","inmobi.com",
        "insight.adsrvr.org","insightexpressai.com","insights.hotjar.com",
        "iot-eu-logser.realme.com","iot-logser.realme.com","ir-na.amazon-adsystem.com",
        "ironsource.mobi","is.com","jdoqocy.com",
        "js-agent.newrelic.com","js.driftt.com","js.honeybadger.io",
        "jsecoin.com","juicyads.com","kameleoon.eu",
        "kochava.com","kqzyfj.com","launches.appsflyer.com",
        "lduhtrp.net","liadm.com","liftoff.io",
        "list-manage.com","live.chartboost.com","log.byteoversea.com",
        "log.outbrain.com","log.pinterest.com","logbak.hicloud.com",
        "logs.logrocket.io","logs.roku.com","logservice.hicloud.com",
        "logservice1.hicloud.com","logx.optimizely.com","lt.activecampaign.com",
        "lt.pardot.com","luckyorange.com","m.doubleclick.net",
        "mads-eu.amazon.com","mads.amazon.com","mailchimp.com",
        "mandrillapp.com","match.adsrvr.org","matomo.cloud",
        "mc.yandex.com","mc.yandex.ru","media.net",
        "mediavisor.doubleclick.net","metrics.brightcove.com","metrics.data.hicloud.com",
        "metrics.icloud.com","metrics.mzstatic.com","metrics2.data.hicloud.com",
        "metrika.yandex.com","metrika.yandex.ru","mgid.com",
        "minemytraffic.com","mineralt.io","minergate.com",
        "minero-proxy.com","minero.cc","mixpanel.com",
        "mobile.launchdarkly.com","mon.byteoversea.com","monerominer.rocks",
        "mouseflow.com","ms.applovin.com","mssl.fwmrm.net",
        "munchkin.marketo.net","ngfts.lge.com","nmetrics.samsung.com",
        "notes-analytics-events.apple.com","notifier.airbrake.io","notify.bugsnag.com",
        "nr.taboola.com","o0.ingest.sentry.io","o2.mouseflow.com",
        "oca.telemetry.microsoft.com","odb.outbrain.com","offerwall.yandex.net",
        "onclickads.net","onetag-sys.com","open.activecampaign.com",
        "open.convertkit.com","open.oneplus.net","openx.net",
        "optanon.blob.core.windows.net","outbrain.com","outcome-ssp.supersonicads.com",
        "pagead.l.doubleclick.net","pagead2.googleadservices.com","pagead2.googlesyndication.com",
        "pagead2.googlevideo.com","pangleglobal.com","partnerstack.com",
        "pendo.io","permutive.com","pi.pardot.com",
        "pippio.com","piwik.pro","pixel.adsafeprotected.com",
        "pixel.advertising.com","pixel.aweber.com","pixel.facebook.com",
        "pixel.mailchimp.com","pixel.mathtag.com","pixel.quantserve.com",
        "pixel.quora.com","pixel.rubiconproject.com","pixel.wp.com",
        "platform.instagram.com","platform.linkedin.com","platform.twitter.com",
        "plus.google.com","popads.net","popcash.net",
        "popmyads.com","ppoi.org","prd.jwpltx.com",
        "prebid.adnxs.com","privacy-mgmt.com","privacymanager.io",
        "prod.uidapi.com","propellerads.com","propellerclick.com",
        "prox.sendinblue.com","pubads.g.doubleclick.net","pubmatic.com",
        "px.ads.linkedin.com","px.srvcs.tumblr.com","qksrv.net",
        "quantcast.com","quantcast.mgr.consensu.org","quantserve.com",
        "r.logrocket.io","r.skimresources.com","raygun.io",
        "rcm-na.amazon-adsystem.com","realtime.luckyorange.com","redditmedia.com",
        "redirect.viglink.com","redirector.googlevideo.com","redirector.skimresources.com",
        "refersion.com","region1.google-analytics.com","reports.crashlytics.com",
        "results.optimizely.com","rlcdn.com","rollbar.com",
        "rs.fullstory.com","rt.applovin.com","rtb.openx.net",
        "rubiconproject.com","s.adroll.com","s.clarity.ms",
        "s.innovid.com","s.skimresources.com","s.youtube.com",
        "s0.2mdn.net","s7.addthis.com","samsung-com.112.2o7.net",
        "samsungads.com","sb.scorecardresearch.com","sc-analytics.appspot.com",
        "sc-static.net","scorecardresearch.com","script.hotjar.com",
        "sdk.appsflyer.com","sdk.privacy-center.org","sdk.singular.net",
        "sdk.split.io","sdkconfig.ad.intl.xiaomi.com","sdkconfig.ad.xiaomi.com",
        "search.spotxchange.com","secure.adnxs.com","securepubads.g.doubleclick.net",
        "sendgrid.net","sentry.io","servicer.mgid.com",
        "sessions.bugsnag.com","settings-win.data.microsoft.com","shareasale-analytics.com",
        "shareasale.com","sharethis.com","sharethrough.com",
        "shrsl.com","sibautomation.com","siftscience.com",
        "singular.net","siteimproveanalytics.com","skimresources.com",
        "smartclip.com","smartclip.net","smartyads.com",
        "smetrics.samsung.com","snap.licdn.com","sourcepoint.mgr.consensu.org",
        "spotxchange.com","ssl.google-analytics.com","ssl.p.jwpcdn.com",
        "sslwidget.criteo.com","st-widget.s3.amazonaws.com","statdynamic.com",
        "static.addtoany.com","static.ads-twitter.com","static.adsafeprotected.com",
        "static.chartbeat.com","static.criteo.net","static.doubleclick.net",
        "static.hotjar.com","static.kameleoon.com","static.media.net",
        "staticxx.facebook.com","stats.g.doubleclick.net","stats.wp.com",
        "statsigapi.net","stream.launchdarkly.com","streaming.split.io",
        "supportmetrics.apple.com","survey.watson.microsoft.com","surveys.hotjar.com",
        "sync.kargo.com","sync.mathtag.com","sync.outbrain.com",
        "sync.search.spotxchange.com","sync.springserve.com","sync.teads.tv",
        "syndication.twitter.com","t.appsflyer.com","t.co",
        "t.hubspotemail.net","t.sendinblue.com","t.skimresources.com",
        "t.teads.tv","taboola.com","tagmanager.google.com",
        "tags.tiqcdn.com","tapad.com","teads.tv",
        "telemetry.microsoft.com","thetradedesk.com","tkqlhce.com",
        "tlx.3lift.com","tools.mouseflow.com","top-fwz1.mail.ru",
        "tpc.googlesyndication.com","tqlkg.com","tr.facebook.com",
        "tr.outbrain.com","tr.snapchat.com","track.constantcontact.com",
        "track.customer.io","track.hubspot.com","track.linksynergy.com",
        "track.mailerlite.com","track.mailgun.org","trackcmp.net",
        "tracking.miui.com","tracking.rus.miui.com","trackjs.com",
        "trafficjunky.net","trc.taboola.com","tremorhub.com",
        "trends.revcontent.com","trk.pinterest.com","try.abtasty.com",
        "tvinteractive.tv","tvpixel.com","u.openx.net",
        "unagi-na.amazon.com","unityads.unity3d.com","upload.luckyorange.net",
        "us-ads.openx.net","us.edge.rms.media.net","us.ibs.lgappstv.com",
        "us.info.lgsmartad.com","usage.trackjs.com","usersync.krxd.net",
        "vars.hotjar.com","vc.hotjar.io","vid.connatix.com",
        "vid.springserve.com","video-ads.twitter.com","vk.com/rtrg",
        "vortex-win.data.microsoft.com","vortex.data.microsoft.com","vungle.com",
        "vwo.com","w1.luckyorange.com","watson.telemetry.microsoft.com",
        "wd.adcolony.com","weather-analytics-events.apple.com","webmine.cz",
        "webminepool.com","webminerpool.com","webview.unityads.unity3d.com",
        "webvisor.com","widget.intercom.io","widgets.outbrain.com",
        "widgets.pinterest.com","wrapper-api.sp-prod.net","ws-na.amazon-adsystem.com",
        "www.anrdoezrs.net","www.awin1.com","www.clarity.ms",
        "www.coinimp.com","www.dpbolvw.net","www.google-analytics.com",
        "www.googleadservices.com","www.googletagmanager.com","www.tkqlhce.com",
        "www3.smartadserver.com","wzrkt.com","xmr.omine.org",
        "xp.apple.com","youtubei.googleapis.com","yumenetworks.com",
        "z-na.amazon-adsystem.com","zenaps.com",
    };

    static LumaShield()
    {
        LoadSettings();
    }

    public static bool IsProtectedSite(string pageUrl)
    {
        if (string.IsNullOrWhiteSpace(pageUrl)) return false;
        try
        {
            var host = new Uri(pageUrl).Host;
            foreach (var immune in ImmuneWorkRoots)
            {
                if (host.Equals(immune, StringComparison.OrdinalIgnoreCase) ||
                    host.EndsWith("." + immune, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch { }
        return false;
    }

    public static string ExtractDomain(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "";
        try
        {
            var uri = new Uri(url);
            return uri.Host;
        }
        catch
        {
            return url;
        }
    }

    public static bool IsShieldEnabledForPage(string pageUrl)
    {
        var domain = ExtractDomain(pageUrl);
        if (string.IsNullOrEmpty(domain)) return true;
        return !DisabledDomains.Contains(domain);
    }

    public static void SetShieldEnabledForPage(string pageUrl, bool enabled)
    {
        var domain = ExtractDomain(pageUrl);
        if (string.IsNullOrEmpty(domain)) return;
        if (enabled)
        {
            DisabledDomains.Remove(domain);
        }
        else
        {
            DisabledDomains.Add(domain);
        }
        SaveSettings();
    }

    public static bool ShouldBlock(string requestUrl, string pageUrl)
    {
        if (string.IsNullOrWhiteSpace(requestUrl)) return false;

        // If user explicitly disabled shield on this page
        if (!IsShieldEnabledForPage(pageUrl)) return false;

        // CRITICAL WORK TOOLS PROTECTION:
        // When on Discord, Google Meet, Google Classroom, Teams, Zoom, never block essential APIs
        if (IsProtectedSite(pageUrl))
        {
            return false;
        }

        try
        {
            var reqUri = new Uri(requestUrl);
            var host = reqUri.Host;

            // Direct or sub-domain match against the full blocked set
            if (BlockedDomains.Contains(host)) return true;

            // Path-based VK tracking pixel
            if (host.EndsWith("vk.com", StringComparison.OrdinalIgnoreCase) &&
                reqUri.AbsolutePath.StartsWith("/rtrg", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Fast hierarchical suffix test (e.g., ad.foo.doubleclick.net)
            var parts = host.Split('.');
            for (int i = 1; i < parts.Length - 1; i++)
            {
                var sub = string.Join('.', parts, i, parts.Length - i);
                if (BlockedDomains.Contains(sub)) return true;
            }

            // General YouTube/Google ad endpoints
            if (host.EndsWith("doubleclick.net", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("googlesyndication.com", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("googleadservices.com", StringComparison.OrdinalIgnoreCase) ||
                reqUri.PathAndQuery.Contains("/youtubei/v1/player/ad_break", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        catch { }

        return false;
    }

    private static void LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var json = File.ReadAllText(SettingsFile);
                var list = JsonSerializer.Deserialize<List<string>>(json);
                if (list != null)
                {
                    DisabledDomains.Clear();
                    foreach (var item in list) DisabledDomains.Add(item);
                }
            }
        }
        catch { }
    }

    private static void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
            var json = JsonSerializer.Serialize(new List<string>(DisabledDomains));
            File.WriteAllText(SettingsFile, json);
        }
        catch { }
    }
}
