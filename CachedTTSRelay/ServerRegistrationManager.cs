using IPinfo;
using IPinfo.Models;
using Newtonsoft.Json;
using RoleplayingVoiceCore;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Numerics;

namespace CachedTTSRelay {
    public class ServerRegistrationManager {
        private NPCVoiceManager _mediaManager;
        string _serverIdentifier = "";
        string _primaryRelayServer = "";
        ConcurrentDictionary<string, ConcurrentDictionary<string, ServerRegistrationRequest>> _serverRegionList = new ConcurrentDictionary<string, ConcurrentDictionary<string, ServerRegistrationRequest>>();
        ConcurrentDictionary<string, ServerRegistrationRequest> _serverList = new ConcurrentDictionary<string, ServerRegistrationRequest>();
        private ServerRegistrationRequest _request;
        public string ServerIdentifier { get => _serverIdentifier; set => _serverIdentifier = value; }
        public string PrimaryRelayServer { get => _primaryRelayServer; set => _primaryRelayServer = value; }

        public ServerRegistrationManager(string serverIdentifier, string primaryServerRelay) {
            Initialize(serverIdentifier, primaryServerRelay);
        }

        private async void Initialize(string serverIdentifier, string primaryServerRelay) {
            _mediaManager = new NPCVoiceManager(null, null, "", "", true);
            _serverIdentifier = serverIdentifier;
            _primaryRelayServer = primaryServerRelay;
            string jsonConfig = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            _request = new ServerRegistrationRequest();
            if (File.Exists(jsonConfig)) {
                _request = JsonConvert.DeserializeObject<ServerRegistrationRequest>(File.ReadAllText(jsonConfig));
            }
            var ipInfo = await GetHardwareLocation(GetPublicIp().ToString());
            _request.Region = ipInfo.Region;
            float y = float.Parse(ipInfo.Latitude);
            float x = float.Parse(ipInfo.Longitude);
            _request.HardwareRegionLocation = new System.Numerics.Vector2(x, y);
            if (string.IsNullOrEmpty(_request.PublicHostAddress)) {
                _request.PublicHostAddress = GetPublicIp().ToString();
            }
            if (string.IsNullOrEmpty(_request.UniqueIdentifier)) {
                _request.UniqueIdentifier = serverIdentifier;
            }
            if (string.IsNullOrEmpty(_request.Alias)) {
                _request.Alias = ipInfo.Country + "-" + serverIdentifier;
            }
            _request.GetNearestIp = false;
            HttpListener ttsListener = new HttpListener();
            ttsListener.Prefixes.Add("http://*:5677/");
            try {
                ttsListener.Start();
            } catch {
                Console.WriteLine("TTS Listener Failed To Run");
            }
            if (Environment.MachineName == "ARTEMISDIALOGUE") {
                _ = Task.Run(() => {
                    Console.WriteLine("Starting Server Registration Service");
                    while (true) {
                        try {
                            HttpListenerContext ctx = ttsListener.GetContext();
                            Task.Run(async () => {
                                try {
                                    using (HttpListenerResponse resp = ctx.Response) {
                                        using (StreamReader reader = new StreamReader(ctx.Request.InputStream)) {
                                            string json = reader.ReadToEnd();
                                            ServerRegistrationRequest request = JsonConvert.DeserializeObject<ServerRegistrationRequest>(json);
                                            if (!request.GetNearestIp) {
                                                if (string.IsNullOrEmpty(request.PublicHostAddress)) {
                                                    request.PublicHostAddress = ctx.Request.RemoteEndPoint.Address.ToString();
                                                }
                                                if (string.IsNullOrEmpty(request.Port)) {
                                                    request.Port = "5670";
                                                }
                                                if (await _mediaManager.VerifyServer(request.PublicHostAddress, request.Port)) {
                                                    AddServerEntry(request);
                                                    ctx.Response.StatusCode = (int)HttpStatusCode.OK;
                                                }
                                            } else {
                                                ctx.Response.StatusCode = (int)HttpStatusCode.OK;
                                                try {
                                                    var data = await GetHardwareLocation(ctx.Request.RemoteEndPoint.Address.ToString());
                                                    request.HardwareRegionLocation = new Vector2(float.Parse(data.Longitude), float.Parse(data.Latitude));
                                                } catch {

                                                }
                                                var closestServer = GetServerEntry(request);
                                                string serverHostData = JsonConvert.SerializeObject(closestServer);
                                                using (StreamWriter writer = new StreamWriter(resp.OutputStream)) {
                                                    await writer.WriteAsync(serverHostData);
                                                    await writer.FlushAsync();
                                                }
                                            }
                                            resp.Close();
                                        }
                                    }
                                } catch (Exception e) {
                                    Console.WriteLine(e.ToString());
                                }
                            });
                        } catch {

                        }
                    }
                });
            }
            _ = Task.Run(async () => {
                Console.WriteLine("Registering Server Heartbeat");
                while (true) {
                    using (HttpClient httpClient = new HttpClient()) {
                        httpClient.BaseAddress = new Uri(_primaryRelayServer);
                        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                        httpClient.Timeout = new TimeSpan(0, 6, 0);
                        var post = await httpClient.PostAsync(httpClient.BaseAddress, new StringContent(JsonConvert.SerializeObject(_request)));
                        if (post.StatusCode == HttpStatusCode.OK) {
                            Console.WriteLine("Server registration updated.");
                        }
                    }
                    Thread.Sleep(60000);
                }
            });
            _ = Task.Run(async () => {
                while (true) {
                    Console.WriteLine("Checking for old server entries");
                    AddServerEntry(_request);
                    foreach (var serverEntries in _serverRegionList) {
                        List<string> oldEntries = new List<string>();
                        foreach (var entry in serverEntries.Value) {
                            if (DateTime.UtcNow.Subtract(new DateTime(entry.Value.LastResponse)).TotalMinutes > 120000) {
                                oldEntries.Add(entry.Key);
                            }
                        }
                        foreach (var oldEntry in oldEntries) {
                            serverEntries.Value.TryRemove(oldEntry, out var value);
                            _serverList.TryRemove(oldEntry, out value);
                        }
                    }
                    Thread.Sleep(65000);
                }
            });
        }

        private ServerRegistrationRequest GetServerEntry(ServerRegistrationRequest request) {
            ServerRegistrationRequest closestServer = null;
            float lastClosestDistance = float.MaxValue;
            foreach (var entry in _serverList) {
                float nextDistance = Math.Abs(Vector2.Distance(request.HardwareRegionLocation, entry.Value.HardwareRegionLocation));
                if (closestServer == null || nextDistance < lastClosestDistance) {
                    closestServer = entry.Value;
                    lastClosestDistance = Vector2.Distance(request.HardwareRegionLocation, closestServer.HardwareRegionLocation);
                }
            }
            return closestServer;
        }

        private void AddServerEntry(ServerRegistrationRequest request) {
            request.LastResponse = DateTime.UtcNow.Ticks;
            if (!_serverRegionList.ContainsKey(request.Region)) {
                _serverRegionList[request.Region] = new ConcurrentDictionary<string, ServerRegistrationRequest>();
            }
            _serverRegionList[request.Region][request.UniqueIdentifier] = request;
            _serverList[request.UniqueIdentifier] = request;
            Console.WriteLine("Heartbeat received from " + request.Alias);
        }

        public static System.Net.IPAddress GetPublicIp(string serviceUrl = "https://ipinfo.io/ip") {
            return System.Net.IPAddress.Parse(new System.Net.WebClient().DownloadString(serviceUrl));
        }

        public async static Task<IPResponse> GetHardwareLocation(string ip) {
            // initializing IPinfo client
            string token = "ed75487d525930";
            IPinfoClient client = new IPinfoClient.Builder()
                .AccessToken(token)
                .Build();
            return await client.IPApi.GetDetailsAsync(ip);
        }
    }
}
