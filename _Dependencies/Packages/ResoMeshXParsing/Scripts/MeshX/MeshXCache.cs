using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ResoMeshXParsing {
    public class MeshXCache {
        private static MeshXCache instance;
        private static readonly object lockObj = new object();
        public string cacheDirectory = "%USERPROFILE%/AppData/Local/Temp/Yellow Dog Man Studios/Resonite";
        public string dataDirectory = "%USERPROFILE%/AppData/LocalLow/Yellow Dog Man Studios/Resonite";
        private Dictionary<string, string> cachedUriPaths = new Dictionary<string, string>();
        private Dictionary<string, Mesh> cachedMeshData = new Dictionary<string, Mesh>();
        private static readonly ThreadLocal<SHA256> sha256Pool = new ThreadLocal<SHA256>(() => SHA256.Create(), trackAllValues: false);
        private static readonly char[] hexChars = "0123456789abcdef".ToCharArray();
        public bool isConnected = false;

        public static MeshXCache Instance {
            get {
                if (instance == null) {
                    lock (lockObj) {
                        if (instance == null) {
                            instance = new MeshXCache();
                        }
                    }
                }
                return instance;
            }
        }
        
        public void AddToMeshDataCache(string id, Mesh data) {
            if (string.IsNullOrEmpty(id) || data == null) {
                return;
            }
            string normalizedId = id.ToLowerInvariant();
            cachedMeshData[normalizedId] = data;
        }

        public Mesh GetFromMeshDataCache(string id) {
            if (string.IsNullOrEmpty(id)) {
                return null;
            }
            string normalizedId = id.ToLowerInvariant();
            if (cachedMeshData.TryGetValue(normalizedId, out Mesh data)) {
                return data;
            }
            return null;
        }

        //TODO: This method is very similar to UpdateTexturePathCache, refactor it in the future
        public async Task UpdatePathCache(Action<string, float> progressCallback = null) {
            if (!PathExists(cacheDirectory)) {
                progressCallback?.Invoke("Cache directory does not exist", 1f);
                return;
            }

            int perTaskTimeoutMs = 10000;
            List<string> files = new List<string>();

            foreach (string file in Directory.EnumerateFiles($"{cacheDirectory}/Cache", "*", SearchOption.AllDirectories)) {
                if (!isConnected) return;

                string extension = Path.GetExtension(file);

                if (extension == ".webp" || !string.IsNullOrEmpty(extension)) continue;
                files.Add(file);
            }

            int completed = 0;
            if (files.Count == 0) {
                progressCallback?.Invoke("No files to process", 0f);
                return;
            }

            (string, string)[] results = new (string hash, string file)[files.Count];
            SynchronizationContext unityContext = SynchronizationContext.Current;

            List<Task> tasks = new List<Task>();
            for (int i = 0; i < files.Count; i++) {
                if (!isConnected) return;
                int index = i;
                string file = files[index];
                tasks.Add(Task.Run(async () => {
                    if (!isConnected) return;
                    Task workTask = Task.Run(() => {
                        try {
                            using (FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, FileOptions.SequentialScan)) {
                                byte[] header = new byte[6];
                                if (fs.Read(header, 0, 6) < 6 || !MeshXHelper.Instance.IsMeshXHeader(header)) {
                                    results[index] = (null, null);
                                    return;
                                }

                                fs.Position = 0;
                                byte[] hashBytes = sha256Pool.Value.ComputeHash(fs);
                                string hash = ByteArrayToHexString(hashBytes);
                                results[index] = (hash, file);

                                if (!string.IsNullOrEmpty(hash) && !string.IsNullOrEmpty(file)) {
                                    cachedUriPaths[hash] = file;
                                }
                            }
                        } catch {
                            results[index] = (null, null);
                        }
                    });

                    Task timeoutTask = Task.Delay(perTaskTimeoutMs);
                    Task finishedTask = await Task.WhenAny(workTask, timeoutTask);

                    if (finishedTask == timeoutTask) {
                        results[index] = (null, null);
                    }

                    int done = Interlocked.Increment(ref completed);

                    unityContext?.Post(_ => {
                        float percent = (float)done / files.Count;
                        progressCallback?.Invoke($"Processing meshx cache... {done} files", percent);
                    }, null);
                }));
            }

            await Task.WhenAll(tasks);
        }

        private static string ByteArrayToHexString(byte[] bytes) {
            char[] chars = new char[bytes.Length * 2];
            for (int i = 0; i < bytes.Length; i++) {
                int b = bytes[i];
                chars[i * 2] = hexChars[b >> 4];
                chars[i * 2 + 1] = hexChars[b & 0xF];
            }
            return new string(chars);
        }

        public string GetFromCache(string id) {
            if (string.IsNullOrEmpty(id)) {
                return null;
            }

            string normalizedId = id.ToLowerInvariant();
            
            if (cachedUriPaths.TryGetValue(normalizedId, out string cachedPath) && PathExists(cachedPath)) {
                return cachedPath;
            }
            
            return null;
        }

        public bool PathExists(string path) {
            return File.Exists(path) || Directory.Exists(path);
        }

        public bool IsCacheDirConnected() {
            return PathExists($"{cacheDirectory}/Cache");
        }

        public bool IsDataDirConnected() {
            return PathExists($"{dataDirectory}/Assets");
        }
    }
}