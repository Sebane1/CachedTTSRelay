using Newtonsoft.Json;
using RoleplayingVoiceCore;
using RoleplayingVoiceDalamud.Datamining;
using RoleplayingVoiceDalamud.Voice;
using System.Diagnostics;
using System.Net;
using static RoleplayingVoiceCore.NPCVoiceManager;

namespace CachedTTSRelay {
    internal class Program {
        private static string _version;
        private static ServerRegistrationManager _serverRegistrationManager;
        private static ServerRegistrationRequest _request;
        private static NPCVoiceManager _mediaManager;

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
            bool shouldContinue = true;
            _version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";
            Console.WriteLine("Version: " + _version);
            
            if (args.Length <= 1) {
                shouldContinue = CheckForUpdates().Result;
            }
            
            if (shouldContinue) {
                StartServerListService();
                StartAudioRelay();
                StartInformationService();
                while (true) {
                    Thread.Sleep(60000);
                }
            }
        }

        private static async Task<bool> CheckForUpdates() {
            try {
                using var client = new HttpClient();
                var updateInfo = await client.GetStringAsync("https://raw.githubusercontent.com/Sebane1/CachedTTSRelay/update.json");
                var updateData = JsonConvert.DeserializeObject<UpdateInfo>(updateInfo);
                
                if (Version.Parse(updateData.Version) > Version.Parse(_version)) {
                    Console.WriteLine($"New version {updateData.Version} available!");
                    
                    // Download the new version
                    var newVersion = await client.GetByteArrayAsync(updateData.DownloadUrl);
                    var updateScript = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "update.sh");
                    var newVersionPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "new-version");
                    
                    await File.WriteAllBytesAsync(newVersionPath, newVersion);
                    
                    // Create platform-specific update script
                    if (OperatingSystem.IsLinux()) {
                        await File.WriteAllTextAsync(updateScript, 
                            $"""
                            #!/bin/bash
                            chmod +x "{newVersionPath}"
                            mv "{newVersionPath}" "{Process.GetCurrentProcess().MainModule?.FileName}"
                            exec "{Process.GetCurrentProcess().MainModule?.FileName}"
                            """);
                    } else if (OperatingSystem.IsWindows()) {
                        updateScript = Path.ChangeExtension(updateScript, ".cmd");
                        await File.WriteAllTextAsync(updateScript, 
                            $"""
                            @echo off
                            move /y "{newVersionPath}" "{Process.GetCurrentProcess().MainModule?.FileName}"
                            start "" "{Process.GetCurrentProcess().MainModule?.FileName}"
                            """);
                    }

                    // Make the script executable on Linux
                    if (OperatingSystem.IsLinux()) {
                        Process.Start("chmod", $"+x {updateScript}").WaitForExit();
                    }

                    // Start the update script and exit
                    Process.Start(updateScript);
                    return false;
                }
                return true;
            } catch (Exception ex) {
                Console.WriteLine($"Update check failed: {ex.Message}");
                return true; // Continue running current version if update check fails
            }
        }

        public class UpdateInfo
        {
            public string Version { get; set; }
            public string DownloadUrl { get; set; }
        }

        private static void StartInformationService() {
            HttpListener informationListener = new HttpListener();
            informationListener.Prefixes.Add("http://*:5684" + @"/");
            try {
                informationListener.Start();
            } catch {
                Console.WriteLine("Information Server Failed");
            }
            _ = Task.Run(() => {
                Console.WriteLine("Information Server Started");
                while (true) {
                    HttpListenerContext ctx = informationListener.GetContext();
                    Task.Run(async () => {
                        using (HttpListenerResponse resp = ctx.Response) {
                            using (BinaryReader reader = new BinaryReader(ctx.Request.InputStream)) {
                                string json = reader.ReadString();
                                InformationRequest request = JsonConvert.DeserializeObject<InformationRequest>(json);
                                switch (request.InformationRequestType) {
                                    case InformationRequestType.GetVoiceLineList:
                                        string voiceLineList = JsonConvert.SerializeObject(_mediaManager.CharacterVoices);
                                        using (StreamWriter streamWriter = new StreamWriter(resp.OutputStream)) {
                                            streamWriter.Write(voiceLineList);
                                        }
                                        break;
                                    case InformationRequestType.UploadVoiceLines:
                                        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, request.Name + ".zip");
                                        using (FileStream fileStream = new FileStream(path, FileMode.Create, FileAccess.Write)) {
                                            await ctx.Request.InputStream.CopyToAsync(fileStream);
                                        }
                                        break;
                                }
                            }
                        }
                    });
                }
            });
        }

        private static void StartServerListService() {
            string id = NPCVoiceManager.CreateMD5(Environment.MachineName + Environment.UserName + Environment.ProcessPath);
            _serverRegistrationManager = new ServerRegistrationManager(id, "http://ai.hubujubu.com:5677");
        }

        private static void StartAudioRelay() {
            Task.Run(async () => {
                _mediaManager = new NPCVoiceManager(await NPCVoiceMapping.GetVoiceMappings(), await NPCVoiceMapping.GetCharacterToCacheType(),
                AppDomain.CurrentDomain.BaseDirectory, "7fe29e49-2d45-423d-8efc-d8e2c1ceaf6d", true);
                string jsonConfig = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
                if (File.Exists(jsonConfig)) {
                    _request = JsonConvert.DeserializeObject<ServerRegistrationRequest>(File.ReadAllText(jsonConfig));
                } else {
                    _request = new ServerRegistrationRequest();
                    _request.Port = "5670";
                }
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
                                                    var generatedLine = await _mediaManager.GetCharacterAudio(resp.OutputStream,
                                                     request.Text, request.UnfilteredText, request.RawText,
                                                     request.Character, genderBool,
                                                     request.Voice, false, GetVoiceModel(request.Model), request.ExtraJsonData, request.RedoLine,
                                                     request.Override, request.VoiceLinePriority == VoiceLinePriority.Ignore, request.VoiceLinePriority,
                                                     NPCVoiceMapping.CheckIfCacheOnly(), resp);
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
