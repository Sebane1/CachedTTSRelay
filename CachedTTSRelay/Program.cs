using Newtonsoft.Json;
using RoleplayingMediaCore;
using RoleplayingVoiceCore;
using RoleplayingVoiceDalamud.Datamining;
using RoleplayingVoiceDalamud.Voice;
using System.Diagnostics;
using System.Net;
using static RoleplayingVoiceCore.NPCVoiceManager;

namespace CachedTTSRelay {
    internal class Program {
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
            Task.Run(async () => {
                NPCVoiceManager mediaManager = new NPCVoiceManager(await NPCVoiceMapping.GetVoiceMappings(), await NPCVoiceMapping.GetCharacterToCacheType(),
                            AppDomain.CurrentDomain.BaseDirectory, "7fe29e49-2d45-423d-8efc-d8e2c1ceaf6d");
                HttpListener ttsListener = new HttpListener();
                //ttsListener.Prefixes.Add("https://ai.hubujubu.com:5670/");
                //ttsListener.Prefixes.Add("http://10.0.0.21:5670/");
                ttsListener.Prefixes.Add("http://localhost:5670/");
                try {
                    ttsListener.Start();
                } catch {
                    Console.WriteLine("TTS Listener Failed To Run");
                }
                _ = Task.Run(() => {
                    Stopwatch saveCooldown = new Stopwatch();
                    saveCooldown.Start();
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
                                            if (request != null) {
                                                if (request.VoiceLinePriority != VoiceLinePriority.SendNote && request.VoiceLinePriority != VoiceLinePriority.Datamining) {
                                                    Stream stream = null;
                                                    int giveUpTimer = 0;
                                                    //while ((stream == null || stream.Length == 0) && giveUpTimer++ < 10) {
                                                    var generatedLine = await mediaManager.GetCharacterAudio(request.Text, request.UnfilteredText, request.RawText,
                                                     request.Character, !JsonConvert.DeserializeObject<ReportData>(request.ExtraJsonData).gender, request.Voice, false,
                                                     GetVoiceModel(request.Model), request.ExtraJsonData, request.RedoLine, request.Override, request.VoiceLinePriority == VoiceLinePriority.Ignore,
                                                     request.VoiceLinePriority);
                                                    resp.StatusCode = (int)HttpStatusCode.OK;
                                                    resp.StatusDescription = generatedLine.Item3;
                                                    stream = generatedLine.Item1;
                                                    if (generatedLine.Item1 != null && resp != null && resp.OutputStream != null) {
                                                        await generatedLine.Item1.CopyToAsync(resp.OutputStream);
                                                    }
                                                    Thread.Sleep(300);
                                                    //}
                                                }
                                                Console.WriteLine("TTS processed and sent! " + profilingTimer.Elapsed);
                                                profilingTimer.Stop();
                                                await resp.OutputStream.FlushAsync();
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
            while (true) {
                Thread.Sleep(60000);
            }
        }
    }
}
