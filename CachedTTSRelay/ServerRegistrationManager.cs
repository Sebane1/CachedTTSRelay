using Newtonsoft.Json;
using RoleplayingVoiceCore;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;

namespace CachedTTSRelay {
    public class ServerRegistrationManager {
        private NPCVoiceManager _mediaManager;
        string _serverIdentifier = "";
        string _primaryRelayServer = "";
        ConcurrentDictionary<string, Dictionary<string, ServerRegistrationRequest>> _serverList = new ConcurrentDictionary<string, Dictionary<string, ServerRegistrationRequest>>();
        public ServerRegistrationManager(string serverIdentifier) {
            _mediaManager = new NPCVoiceManager(null, null, "", "");
            _serverIdentifier = serverIdentifier;
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
                                    if (string.IsNullOrEmpty(request.PublicHostAddress)) {
                                        request.PublicHostAddress = ctx.Request.RemoteEndPoint.Address.ToString();
                                    }
                                    if (await _mediaManager.VerifyServer(request.PublicHostAddress, request.Port)) {
                                        _serverList[request.Region][request.UniqueIdentifier] = request;
                                        ctx.Response.StatusCode = (int)HttpStatusCode.OK;
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
                Console.WriteLine("Server started");
                while (true) {
                    using (HttpClient httpClient = new HttpClient()) {
                        ServerRegistrationRequest request = new ServerRegistrationRequest();
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
        }

        public string ServerIdentifier { get => _serverIdentifier; set => _serverIdentifier = value; }
    }
}
