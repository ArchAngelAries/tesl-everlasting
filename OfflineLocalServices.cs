using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using SparkleServices;
using UnityEngine;

namespace OfflineEngine
{
    // Local implementation of the original client's HTTP services (SparkleServices),
    // served on loopback so the unmodified native boot, menus and story flow talk real
    // HTTP/JSON to it. Responses use the client's own [Serializable] result classes via
    // JsonUtility (thread-safe), so field names match the original wire format exactly.
    // The router is transport-agnostic so a later standalone/self-hosted server can reuse
    // it. Every request is logged (method, path, status) without bodies or tokens.
    public sealed class OfflineLocalServices
    {
        public delegate string Handler(string method, string path, string body);

        public static OfflineLocalServices Instance { get; private set; }
        public int Port { get; private set; }
        public string RootUrl { get { return "http://127.0.0.1:" + Port; } }
        public string ConfigUrl { get { return RootUrl + "/config.json"; } }

        private readonly HttpListener listener = new HttpListener();
        private readonly Dictionary<string, Handler> routes = new Dictionary<string, Handler>(StringComparer.OrdinalIgnoreCase);
        private readonly string logPath;
        private readonly object logLock = new object();
        private readonly OfflineLocalProfile profile;
        private readonly OfflinePlayerState state;
        private readonly string token = Guid.NewGuid().ToString("N");

        public static OfflineLocalServices Start(string exportDirectory, OfflineLocalProfile profile, OfflinePlayerState state)
        {
            if (Instance != null) return Instance;
            OfflineLocalServices services = new OfflineLocalServices(exportDirectory, profile, state);
            services.Listen();
            Instance = services;
            return services;
        }

        private OfflineLocalServices(string exportDirectory, OfflineLocalProfile profile, OfflinePlayerState state)
        {
            this.profile = profile;
            this.state = state;
            logPath = Path.Combine(exportDirectory, "local-services-requests.tsv");
            File.WriteAllText(logPath, "utc\tmethod\tpath\tstatus\troute\n");
            Register("config.json", Config);
            Register("authDev", AuthDev);
            // Fully formed defaults of the client's own response classes (empty lists,
            // default nested objects, resultCode Ok). Real data handlers replace these.
            foreach (KeyValuePair<string, string> pair in new Dictionary<string, string> {
                { "profile/fetch", "SparkleServices.Profile+FetchProfileResponse" },
                { "inventory/list_v2", "SparkleServices.Inventory+InventoryListResponse" },
                { "packages/list", "SparkleServices.Packages+ListPackagesResponse" },
                { "dailyrewards/list", "SparkleServices.DailyRewards+ListRewardsResponse" },
                { "events/fetch", "SparkleServices.Events+ListEventsResponse" },
                { "servermessages/fetch", "SparkleServices.ServerMessages+FetchResponse" },
                { "campaigns/list", "SparkleServices.Campaign+ListCampaignsResponse" },
                { "rewards/list", "SparkleServices.Rewards+ListRewardsResponse" },
                { "quests/fetch", "SparkleServices.Quests+QuestFetchResponse" },
                { "shop/fetchCatalog", "SparkleServices.Shop+FetchCatalogResponse" },
                { "stats/fetch", "SparkleServices.Stats+FetchStatsResponse" },
                { "arena/fetchActiveArenaInstances", "SparkleServices.Arena+FetchArenaIntancesResult" },
                { "genericinventory/fetch", "SparkleServices.GenericInventory+FetchResponse" },
                { "analytics/reportingConfig", "SparkleServices.Analytics+FetchResponse" },
                { "errorreporting/reportingConfig", "SparkleServices.ErrorReporting+FetchResponse" },
                { "matching/rejoin", "SparkleServices.Matching+ClientMatchRejoinResult" },
                { "beamsocial/v3/friends", "SparkleServices.Friends+FriendsResponse" },
                { "beamsocial/v3/friends/requests", "SparkleServices.Friends+RequestsResponse" },
                { "beamsocial/v3/friends/block", "SparkleServices.Friends+BlockedResponse" } })
                Register(pair.Key, TypedDefault(pair.Value));
            Register("errorreporting/reportError", ReportError);
            Register("profile/fetch", ProfileFetch);
            Register("profile/setOnboardingProgress", SetOnboardingProgress);
            Register("genericinventory/fetch", GenericInventoryFetch);
            Register("genericinventory/setSelected", GenericInventorySetSelected);
            Register("shop/fetchCatalog", ShopCatalog);
            // Session refresh reschedules after timeToRefresh seconds; 0 would loop every frame.
            Register("account/bnetRefreshSession", delegate {
                AuthBase.RefreshSessionResponse refresh = new AuthBase.RefreshSessionResponse();
                refresh.resultCode = ResultCode.Ok; refresh.timeToRefresh = 86400; refresh.sessionToken = token;
                return JsonUtility.ToJson(refresh); });
            // Presence refreshes every time_to_expire/2 seconds; 0 would re-post continuously.
            Register("beampresence/v1/presence", delegate { return "{\"message\":\"ok\",\"code\":200,\"response\":{\"time_to_expire\":3600}}"; });
        }

        public static Handler TypedDefault(string typeName)
        {
            Type type = typeof(ServicesManager).Assembly.GetType(typeName);
            if (type == null) throw new TypeLoadException("Client response type is absent: " + typeName);
            return delegate(string method, string path, string body)
            {
                object value = Activator.CreateInstance(type);
                HTTPUtils.BaseResult result = value as HTTPUtils.BaseResult;
                if (result != null) result.resultCode = ResultCode.Ok;
                return JsonUtility.ToJson(value);
            };
        }

        // Client-side error reports are diagnostics for this restoration: keep them locally.
        private string ReportError(string method, string path, string body)
        {
            lock (logLock)
                File.AppendAllText(Path.Combine(Path.GetDirectoryName(logPath), "local-services-client-errors.txt"),
                    DateTime.UtcNow.ToString("o") + "\n" + (body.Length > 4000 ? body.Substring(0, 4000) : body) + "\n\n");
            return DefaultOk(method, path, body);
        }

        public void Register(string path, Handler handler) { lock (routes) routes[path.Trim('/')] = handler; }

        private void Listen()
        {
            // Loopback only; pick a free port by trying a small range.
            for (int port = 47310; port < 47330; port++)
            {
                try
                {
                    listener.Prefixes.Clear();
                    listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
                    listener.Start();
                    Port = port;
                    break;
                }
                catch (Exception) { }
            }
            if (Port == 0) throw new InvalidOperationException("No loopback port available for local services.");
            Thread thread = new Thread(Loop);
            thread.IsBackground = true;
            thread.Name = "TESL local services";
            thread.Start();
            Debug.Log("OFFLINE SERVICES: listening on " + RootUrl);
        }

        private void Loop()
        {
            while (listener.IsListening)
            {
                HttpListenerContext context;
                try { context = listener.GetContext(); }
                catch (Exception) { return; }
                try { Serve(context); }
                catch (Exception ex) { Log(context.Request.HttpMethod, context.Request.Url.AbsolutePath, 500, "exception " + ex.GetType().Name); TryRespond(context, 500, "{}"); }
            }
        }

        private void Serve(HttpListenerContext context)
        {
            string method = context.Request.HttpMethod;
            string path = context.Request.Url.AbsolutePath.Trim('/');
            string body = "";
            if (context.Request.HasEntityBody)
                using (StreamReader reader = new StreamReader(context.Request.InputStream, Encoding.UTF8)) body = reader.ReadToEnd();
            Handler handler;
            string route = path;
            bool found;
            lock (routes) found = routes.TryGetValue(path, out handler);
            if (!found)
            {
                // "service/action": exact match above, else the default Ok result.
                handler = DefaultOk; route = "default-ok";
            }
            string json = handler(method, path, body);
            // Request shapes are needed to implement handlers; auth bodies are never logged.
            string sample = path.StartsWith("auth", StringComparison.OrdinalIgnoreCase) || body.Length == 0 ? "" :
                (body.Length > 400 ? body.Substring(0, 400) : body).Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
            Log(method, path, 200, route + (sample.Length == 0 ? "" : "\t" + sample));
            TryRespond(context, 200, json);
        }

        private static void TryRespond(HttpListenerContext context, int status, string json)
        {
            try
            {
                byte[] data = Encoding.UTF8.GetBytes(json ?? "{}");
                context.Response.StatusCode = status;
                context.Response.ContentType = "application/json; charset=UTF-8";
                context.Response.ContentLength64 = data.Length;
                context.Response.OutputStream.Write(data, 0, data.Length);
                context.Response.OutputStream.Close();
            }
            catch (Exception) { }
        }

        private void Log(string method, string path, int status, string route)
        {
            lock (logLock)
                File.AppendAllText(logPath, DateTime.UtcNow.ToString("o") + "\t" + method + "\t" + path + "\t" + status + "\t" + route + "\n");
        }

        private static string DefaultOk(string method, string path, string body)
        { return JsonUtility.ToJson(new HTTPUtils.BaseResult { resultCode = ResultCode.Ok }); }

        // Every endpoint group of the original client maps to /<group-lowercase>.
        private string Config(string method, string path, string body)
        {
            StringBuilder json = new StringBuilder("{");
            bool first = true;
            foreach (string name in Enum.GetNames(typeof(Endpoints.EndpointType)))
            {
                if (!first) json.Append(',');
                first = false;
                // FetchConfig formats each value with the config URL's scheme+host as {0}.
                json.Append('"').Append(name).Append("\":\"{0}:").Append(Port).Append('/').Append(name.ToLowerInvariant()).Append('"');
            }
            return json.Append('}').ToString();
        }

        private string AuthDev(string method, string path, string body)
        {
            AuthBase.AuthResult result = new AuthBase.AuthResult();
            result.resultCode = ResultCode.Ok;
            result.data = new AuthData
            {
                token = token, channel = "live", sessionId = Guid.NewGuid().ToString("N"), sessionType = "offline",
                buid = profile.Identity, nickname = profile.Username, kpiId = profile.Identity,
                deviceGrade = "high", bnetCountry = "", timeToRefresh = 86400, maxRefreshFailures = 3,
                secondsBetweenRefreshFailures = 1f, allowTP = false, buildSetId = 0
            };
            return JsonUtility.ToJson(result);
        }

        private string ProfileFetch(string method, string path, string body)
        {
            OfflinePlayerStateData data = state.Snapshot();
            Profile.FetchProfileResponse response = new Profile.FetchProfileResponse();
            response.resultCode = ResultCode.Ok;
            response.profile = new Profile.ProfileStruct
            {
                nickname = profile.Username, level = OfflineLevelService.Level(data.xp), xp = data.xp, gold = data.gold, gems = data.gems,
                tickets = data.tickets, onboardingProgressFlags = data.onboardingProgressFlags
            };
            OfflineRankedService.Fill(state, response.profile);
            return JsonUtility.ToJson(response);
        }

        private string SetOnboardingProgress(string method, string path, string body)
        {
            Profile.SetOnboardingProgressRequest request = JsonUtility.FromJson<Profile.SetOnboardingProgressRequest>(body);
            if (request == null || request.progressType < 0 || request.progressType > 62)
                return JsonUtility.ToJson(new HTTPUtils.BaseResult { resultCode = ResultCode.ClientHttpErrorBadRequest });
            long bit = 1L << (int)request.progressType;
            state.Mutate(delegate(OfflinePlayerStateData d) { d.onboardingProgressFlags |= bit; });
            return DefaultOk(method, path, body);
        }

        // Main-thread cache of installed avatar unique names (config loads after the first
        // fetch); the listener thread only reads this snapshot.
        private static volatile string[] avatarNames = new string[0];
        public static void PublishAvatars(string[] names) { avatarNames = names ?? new string[0]; }
        private static volatile string defaultAvatar = "";
        public static void PublishDefaultAvatar(string name) { defaultAvatar = name ?? ""; }

        private string GenericInventoryFetch(string method, string path, string body)
        {
            state.Mutate(OfflineCosmetics.ClaimPending);
            OfflinePlayerStateData data = state.Snapshot();
            GenericInventory.FetchResponse response = new GenericInventory.FetchResponse();
            response.resultCode = ResultCode.Ok;
            response.categories = new List<GenericInventory.CategoryData>();
            GenericInventory.CategoryData avatars = new GenericInventory.CategoryData();
            avatars.category = "avatar";
            avatars.available = new List<string>(avatarNames);
            avatars.selected = data.GetSelected("avatar") ?? defaultAvatar;
            avatars.locked = new List<string>();
            avatars.descriptions = new List<string>();
            response.categories.Add(avatars);
            response.categories.AddRange(OfflineCosmetics.Categories(data));
            return JsonUtility.ToJson(response);
        }

        private string GenericInventorySetSelected(string method, string path, string body)
        {
            GenericInventory.SetSelectedRequest request = JsonUtility.FromJson<GenericInventory.SetSelectedRequest>(body);
            bool valid = request != null && (request.category == "avatar" ? Array.IndexOf(avatarNames, request.itemKey) >= 0
                : OfflineCosmetics.CanSelect(state.Snapshot(), request.category, request.itemKey));
            if (!valid) return JsonUtility.ToJson(new HTTPUtils.BaseResult { resultCode = ResultCode.ClientHttpErrorBadRequest });
            state.Mutate(delegate(OfflinePlayerStateData d) { d.SetSelected(request.category, request.itemKey); });
            return DefaultOk(method, path, body);
        }

        // The client keeps ChannelCatalog null for an empty item list, and the main menu's
        // sale widget then dereferences it. Offer the original gold-priced Core Set pack
        // ("packs_core_1", the key the shop tutorial uses); purchases arrive with the pack store.
        private string ShopCatalog(string method, string path, string body)
        {
            Shop.FetchCatalogResponse response = new Shop.FetchCatalogResponse();
            response.resultCode = ResultCode.Ok;
            response.catalog = new Shop.Catalog();
            response.catalog.platform = "offline";
            response.catalog.currencyCode = "";
            response.catalog.items = new List<Shop.CatalogItem>();
            int index = 0;
            if (OfflineModPacks.ListFirst) index = AddModPacks(response.catalog.items, index);
            foreach (OfflinePackService.StoreOffer offer in OfflinePackService.StoreOffers)
            {
                Shop.CatalogItem pack = new Shop.CatalogItem();
                pack.itemKey = offer.ItemKey; pack.platformKey = offer.ItemKey;
                pack.itemState = Shop.CatalogItem.IChannelCatalogItemState.Normal;
                pack.priceGold = offer.Gold; pack.index = index++;
                pack.summaryTitle = offer.Title; pack.detailsTitle = offer.Title;
                pack.summaryDescription = offer.Description; pack.detailsDescription = offer.Description;
                pack.tags = new List<string> { "Pack" };
                // Official bundled store art where the game ships it, else the UESP pack render (OfflineStoreArt).
                string bundled;
                string art = OfflinePackService.StoreArtDirectory == null ? null : Path.Combine(OfflinePackService.StoreArtDirectory, "pack_" + offer.Pool + ".png");
                if (OfflineStoreArt.BundledPackArt.TryGetValue(offer.Pool, out bundled)) pack.imageUrl = bundled;
                else if (art != null && File.Exists(art)) pack.imageUrl = new Uri(art).AbsoluteUri;
                pack.frontPageImageUrl = pack.imageUrl;
                response.catalog.items.Add(pack);
            }
            if (!OfflineModPacks.ListFirst) index = AddModPacks(response.catalog.items, index);
            // Decks, card backs and story acts for gold (OfflineGoldStore).
            try { response.catalog.items.AddRange(OfflineGoldStore.CatalogItems(state.Snapshot(), index)); }
            catch (Exception e) { Debug.LogWarning("OFFLINE GOLD STORE: catalog: " + e.Message); }
            return JsonUtility.ToJson(response);
        }

        // Mod card packs (OfflineModPacks), sold like the game's packs.
        private static int AddModPacks(List<Shop.CatalogItem> items, int index)
        {
            foreach (OfflineModPacks.Pack mp in OfflineModPacks.All)
            {
                Shop.CatalogItem pack = new Shop.CatalogItem();
                pack.itemKey = mp.ItemKey; pack.platformKey = mp.ItemKey;
                pack.itemState = Shop.CatalogItem.IChannelCatalogItemState.Normal;
                pack.priceGold = mp.Gold; pack.index = index++;
                pack.summaryTitle = mp.Name; pack.detailsTitle = mp.Name;
                pack.summaryDescription = mp.Description; pack.detailsDescription = mp.Description;
                pack.tags = new List<string> { "Pack" };
                pack.imageUrl = mp.Image; pack.frontPageImageUrl = mp.Image;
                items.Add(pack);
            }
            return index;
        }

        public void Stop()
        {
            try { listener.Stop(); listener.Close(); } catch (Exception) { }
            if (Instance == this) Instance = null;
        }
    }
}
