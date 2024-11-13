using Newtonsoft.Json;
using RoleplayingVoiceCore;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Windows;

namespace CachedTTSRelay {
    public class ServerRegistrationManager {
        private NPCVoiceManager _mediaManager;
        string _serverIdentifier = "";
        string _primaryRelayServer = "";
        ConcurrentDictionary<string, Dictionary<string, ServerRegistrationRequest>> _serverRegionList = new ConcurrentDictionary<string, Dictionary<string, ServerRegistrationRequest>>();
        public ServerRegistrationManager(string serverIdentifier, string primaryServerRelay) {
            _mediaManager = new NPCVoiceManager(null, null, "", "");
            _serverIdentifier = serverIdentifier;
            _primaryRelayServer = primaryServerRelay;
            HttpListener ttsListener = new HttpListener();
            ttsListener.Prefixes.Add("http://*:5677/");
            try {
                ttsListener.Start();
            } catch {
                Console.WriteLine("TTS Listener Failed To Run");
            }
            _ = Task.Run(() => {
                Console.WriteLine("Starting Server Registration Service");
                while (true) {
                    try {
                        HttpListenerContext ctx = ttsListener.GetContext();
                        Task.Run(async () => {
                            using (HttpListenerResponse resp = ctx.Response) {
                                using (StreamReader reader = new StreamReader(ctx.Request.InputStream)) {
                                    string json = reader.ReadToEnd();
                                    ServerRegistrationRequest request = JsonConvert.DeserializeObject<ServerRegistrationRequest>(json);
                                    if (!request.GetList) {
                                        if (string.IsNullOrEmpty(request.PublicHostAddress)) {
                                            request.PublicHostAddress = ctx.Request.RemoteEndPoint.Address.ToString();
                                        }
                                        if (string.IsNullOrEmpty(request.Port)) {
                                            request.Port = "5670";
                                        }
                                        request.LastResponse = DateTime.UtcNow;
                                        if (await _mediaManager.VerifyServer(request.PublicHostAddress, request.Port)) {
                                            if (!_serverRegionList.ContainsKey(request.Region)) {
                                                _serverRegionList[request.Region] = new Dictionary<string, ServerRegistrationRequest>();
                                            }
                                            _serverRegionList[request.Region][request.UniqueIdentifier] = request;
                                            ctx.Response.StatusCode = (int)HttpStatusCode.OK;
                                        }
                                    } else {
                                        ctx.Response.StatusCode = (int)HttpStatusCode.OK;
                                        string serverListData = JsonConvert.SerializeObject(_serverRegionList);
                                        using (StreamWriter writer = new StreamWriter(resp.OutputStream)) {
                                            await writer.WriteAsync(serverListData);
                                            await writer.FlushAsync();
                                        }
                                    }
                                    resp.Close();
                                }
                            }
                        });
                    } catch {

                    }
                }
            });
            _ = Task.Run(async () => {
                Console.WriteLine("Registering Server Heartbeat");
                string jsonConfig = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
                ServerRegistrationRequest request = new ServerRegistrationRequest();
                if (File.Exists(jsonConfig)) {
                    request = JsonConvert.DeserializeObject<ServerRegistrationRequest>(File.ReadAllText(jsonConfig));
                }
                if (string.IsNullOrEmpty(request.Region)) {
                    request.Region = RegionAndLanguageHelper.GetMachineCurrentLocation(5);
                }
                while (true) {
                    using (HttpClient httpClient = new HttpClient()) {
                        request.GetList = false;
                        string jsonRequest = JsonConvert.SerializeObject(request);
                        httpClient.BaseAddress = new Uri(_primaryRelayServer);
                        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                        httpClient.Timeout = new TimeSpan(0, 6, 0);
                        var post = await httpClient.PostAsync(httpClient.BaseAddress, new StringContent(JsonConvert.SerializeObject(jsonRequest)));
                        if (post.StatusCode == HttpStatusCode.OK) {
                            Console.WriteLine("Server registration updated.");
                        }
                    }
                    Thread.Sleep(60000);
                }
            });
            _ = Task.Run(async () => {
                while (true) {
                    Console.WriteLine("Checking for old entries");
                    foreach (var serverEntries in _serverRegionList) {
                        List<string> oldEntries = new List<string>();
                        foreach (var entry in serverEntries.Value) {
                            if (DateTime.UtcNow.Subtract(entry.Value.LastResponse).TotalMinutes > 120000) {
                                oldEntries.Add(entry.Key);
                            }
                        }
                        foreach (var oldEntry in oldEntries) {
                            serverEntries.Value.Remove(oldEntry);
                        }
                    }
                    Thread.Sleep(65000);
                }
            });
        }

        public string ServerIdentifier { get => _serverIdentifier; set => _serverIdentifier = value; }
    }
}
