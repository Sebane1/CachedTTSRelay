using System.Diagnostics;
using System.Net;

namespace CachedTTSRelay {
    internal class Program {
        public static string ReplaceInvalidChars(string filename) {
            return string.Join("_", filename.Split(Path.GetInvalidFileNameChars()));
        }

        static void Main(string[] args) {
            HttpListener ttsListener = new HttpListener();
            ttsListener.Prefixes.Add("https://ai.hubujubu.com:5697/");
            ttsListener.Prefixes.Add("https://10.0.0.21:5697/");
            ttsListener.Prefixes.Add("https://localhost:5697/");
            try {
                ttsListener.Start();
            } catch {
                Console.WriteLine("Elevenlabs Listener Failed To Run");
            }
            Task.Run(() => {
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
                                        VoiceLineRequest request = JsonConvert.DeserializeObject<VoiceLineRequest>(json);
                                        KeyValuePair<bool, string> generatedLine = new KeyValuePair<bool, string>(false, "");
                                        string voiceCacheUsed = string.Empty;
                                        if (request != null) {
                                            if (request.VoiceLinePriority != VoiceLinePriority.SendNote && request.VoiceLinePriority != VoiceLinePriority.Datamining) {
                                                if (request.AggressiveCache) {
                                                    File.OpenRead(
                                                    await _mediaManager.DoVoice(request.Text, request.Voice, false)).CopyTo(resp.OutputStream);
                                                } else {
                                                    generatedLine = await _mediaManager.GetVoiceNoAggressiveCache(request.Text, request.UnfilteredText, request.RawText,
                                                    request.Voice, request.Character, request.Model, request.RedoLine, request.Override, request.VoiceLinePriority, request.VersionIdentifier, request.UseMuteList, resp);
                                                }
                                            }
                                            //voiceCacheUsed = generatedLine.Value;
                                            //resp.Headers.Add("VoiceEngine", voiceCacheUsed);
                                            //resp.StatusDescription = voiceCacheUsed;
                                            //resp.StatusCode = (int)HttpStatusCode.OK;
                                            Console.WriteLine("Check if need to save missing data to log.");

                                            if (request.VoiceLinePriority == VoiceLinePriority.SendNote) {
                                                try {
                                                    Console.WriteLine("Save to log.");
                                                    string characterName = ReplaceInvalidChars(request.Character);
                                                    Directory.CreateDirectory(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"notes\"));
                                                    string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"notes\" + characterName + "_" + CreateMD5(request.Text) + ".json");
                                                    if (!File.Exists(path)) {
                                                        File.WriteAllText(path, request.ExtraJsonData);
                                                    }
                                                } catch (Exception e) {
                                                    Console.WriteLine(e.Message);
                                                }
                                            } else if (!generatedLine.Key && !string.IsNullOrEmpty(request.ExtraJsonData)) {
                                                try {
                                                    Console.WriteLine("Save to log.");
                                                    string characterName = ReplaceInvalidChars(request.Character);
                                                    Directory.CreateDirectory(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"logs\"));
                                                    string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"logs\" + characterName + "_" + CreateMD5(request.Text) + ".json");
                                                    if (!File.Exists(path)) {
                                                        File.WriteAllText(path, request.ExtraJsonData);
                                                    }
                                                } catch (Exception e) {
                                                    Console.WriteLine(e.Message);
                                                }
                                            } else if (!string.IsNullOrEmpty(request.ExtraJsonData)) {
                                                Console.WriteLine("Save to log.");
                                                string characterName = ReplaceInvalidChars(request.Character);
                                                Directory.CreateDirectory(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"datamining\"));
                                                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"datamining\" + characterName + "_" + CreateMD5(request.Text) + ".json");
                                                if (!File.Exists(path)) {
                                                    File.WriteAllText(path, request.ExtraJsonData);
                                                }
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
        }
    }
}
