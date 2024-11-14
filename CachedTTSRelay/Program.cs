using AutoUpdaterDotNET;
using Newtonsoft.Json;
using RoleplayingVoiceCore;
using RoleplayingVoiceDalamud.Datamining;
using RoleplayingVoiceDalamud.Voice;
using System.Diagnostics;
using System.Net;
using System.Windows.Forms;
using static RoleplayingVoiceCore.NPCVoiceManager;

namespace CachedTTSRelay {
    internal class Program {
        private static string _version;
        private static ServerRegistrationManager _serverRegistrationManager;
        private static ServerRegistrationRequest _request;

        public static string ReplaceInvalidChars(string filename) {
            return string.Join("_", filename.Split(Path.GetInvalidFileNameChars()));
        }
        public static VoiceModel GetVoiceModel(string value) {
            switch (value.ToLower()) {
                case "quality":
                    return VoiceModel.Quality;
                case "speed":
                    return VoiceModel.Speed;
                case "cheap":
                    return VoiceModel.Cheap;
            }
            return VoiceModel.Cheap;
        }
        static void Main(string[] args) {
            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            bool launchForm = true;
            _version = Application.ProductVersion.Split('+')[0];
            AutoUpdater.InstalledVersion = new Version(_version);
            AutoUpdater.DownloadPath = Application.StartupPath;
            AutoUpdater.Synchronous = true;
            AutoUpdater.Mandatory = true;
            AutoUpdater.UpdateMode = Mode.ForcedDownload;
            if (args.Length <= 1) {
                AutoUpdater.Start("https://raw.githubusercontent.com/Sebane1/CachedTTSRelay/update.xml");
                AutoUpdater.ApplicationExitEvent += delegate () {
                    launchForm = false;
                };
            }
            if (launchForm) {
                StartServerListService();
                StartAudioRelay();
                while (true) {
                    Thread.Sleep(60000);
                }
            }
        }

        private static void StartServerListService() {
            string id = NPCVoiceManager.CreateMD5(Environment.MachineName + Environment.UserName + Environment.ProcessPath);
            _serverRegistrationManager = new ServerRegistrationManager(id, "http://ai.hubujubu.com:5677");
        }

        private static void StartAudioRelay() {
            Task.Run(async () => {
                string jsonConfig = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
                if (File.Exists(jsonConfig)) {
                    _request = JsonConvert.DeserializeObject<ServerRegistrationRequest>(File.ReadAllText(jsonConfig));
                } else {
                    _request = new ServerRegistrationRequest();
                    _request.Port = "5670";
                }
                NPCVoiceManager mediaManager = new NPCVoiceManager(
                    await NPCVoiceMapping.GetVoiceMappings(), await NPCVoiceMapping.GetCharacterToCacheType(),
                    AppDomain.CurrentDomain.BaseDirectory, "7fe29e49-2d45-423d-8efc-d8e2c1ceaf6d");
                HttpListener ttsListener = new HttpListener();
                ttsListener.Prefixes.Add("http://*:" + _request.Port + @"/");
                try {
                    ttsListener.Start();
                } catch {
                    Console.WriteLine("TTS Listener Failed To Run");
                }
                _ = Task.Run(() => {
                    Console.WriteLine("Server started");
                    while (true) {
                        try {
                            HttpListenerContext ctx = ttsListener.GetContext();
                            Task.Run(async () => {
                                using (HttpListenerResponse resp = ctx.Response) {
                                    using (StreamReader reader = new StreamReader(ctx.Request.InputStream)) {
                                        try {
                                            Stopwatch profilingTimer = Stopwatch.StartNew();
                                            string json = reader.ReadToEnd();
                                            ProxiedVoiceRequest request = JsonConvert.DeserializeObject<ProxiedVoiceRequest>(json);
                                            string voiceCacheUsed = string.Empty;
                                            bool genderBool = !string.IsNullOrEmpty(request.ExtraJsonData) ? JsonConvert.DeserializeObject<ReportData>(request.ExtraJsonData).gender : false;
                                            if (request != null) {
                                                if (request.VoiceLinePriority != VoiceLinePriority.SendNote
                                                && request.VoiceLinePriority != VoiceLinePriority.Datamining) {
                                                    var generatedLine = await mediaManager.GetCharacterAudio(resp.OutputStream,
                                                     request.Text, request.UnfilteredText, request.RawText,
                                                     request.Character, genderBool,
                                                     request.Voice, false, GetVoiceModel(request.Model), request.ExtraJsonData, request.RedoLine,
                                                     request.Override, request.VoiceLinePriority == VoiceLinePriority.Ignore, request.VoiceLinePriority,
                                                     NPCVoiceMapping.CheckIfCacheOnly(), resp);
                                                    await resp.OutputStream.FlushAsync();
                                                    resp.Close();
                                                }
                                                Console.WriteLine("TTS processed and sent! " + profilingTimer.Elapsed);
                                                profilingTimer.Stop();
                                            }
                                        } catch (Exception e) {
                                            Console.WriteLine(e.Message + " " + e);
                                        }
                                    }
                                }
                            });
                        } catch (Exception e) {
                            Console.WriteLine(e.Message);
                        }
                    }
                });
            });
        }
    }
}
