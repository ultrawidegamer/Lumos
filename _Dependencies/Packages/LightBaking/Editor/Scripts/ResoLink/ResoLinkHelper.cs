using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ResoMeshXParsing;
using UnityEditor;
using UnityEngine;

namespace LightBakingResoLink {
    public class ResoLinkHelper {
        private static ResoLinkHelper instance;
        private ResoLinkWebSocket resoLinkWebSocket;
        private static readonly object lockObj = new object();
        private const int MAX_CONCURRENT_RESOLINK_REQUESTS = 10;
        private const int MAX_CONCURRENT_MESH_DOWNLOADS = 10;
        public HierarchyData hierarchyData = null;
        private SynchronizationContext unityContext = SynchronizationContext.Current;
        private Dictionary<string, SlotData> slotDataLookup = null;
        private Dictionary<string, GameObject> createdObjects = null;
        private Dictionary<string, ResoLinkComponentResponse> cachedMeshComponentData = new Dictionary<string, ResoLinkComponentResponse>();

        public static ResoLinkHelper Instance {
            get {
                if (instance == null) {
                    lock (lockObj) {
                        if (instance == null) {
                            instance = new ResoLinkHelper();
                        }
                    }
                }
                return instance;
            }
        }

        private ResoLinkHelper() {
            resoLinkWebSocket = new ResoLinkWebSocket();
        }

        public async Task<bool> ConnectAsync(string url) {
            if (string.IsNullOrEmpty(url)) {
                return false;
            }

            try {
                await resoLinkWebSocket.Connect(url);
                return resoLinkWebSocket.IsConnected();
            } catch (Exception e) {
                Debug.LogError($"Failed to connect to ResoLink: {e.Message}");
                return false;
            }
        }

        public void Disconnect() {
            try {
                resoLinkWebSocket.Disconnect();
            } catch (Exception e) {
                Debug.LogError($"Failed to disconnect from ResoLink: {e.Message}");
            }
        }

        public bool IsConnected() {
            return resoLinkWebSocket != null && resoLinkWebSocket.IsConnected();
        }

        public async Task SendAsync<T>(T data) {
            if (!IsConnected()) return;

            try {
                await resoLinkWebSocket.Send(data);
            } catch (Exception e) {
                Debug.LogError($"Failed to send data to ResoLink: {e.Message}");
            }
        }

        public async Task<ResoLinkResponse> ReceiveAsync() {
            if (!IsConnected()) return null;

            try {
                return await resoLinkWebSocket.Receive<ResoLinkResponse>();
            } catch (Exception e) {
                Debug.LogError($"Failed to receive data from ResoLink: {e.Message}");
                return null;
            }
        }

        public async Task<ResoLinkComponentResponse> ReceiveAsyncComponent() {
            if (!IsConnected()) return null;

            try {
                return await resoLinkWebSocket.Receive<ResoLinkComponentResponse>();
            } catch (Exception e) {
                Debug.LogError($"Failed to receive data from ResoLink: {e.Message}");
                return null;
            }
        }

        public async Task<ResoLinkResponse> FetchSlot(string id, bool withComponents = false) {
            if (!IsConnected()) return null;
            if (id == null) return null;

            try {
                await SendAsync(new GetSlotMessage() { SlotId = id, IncludeComponentData = withComponents });
                return await ReceiveAsync();
            } catch (Exception e) {
                Debug.LogError($"Failed to send and receive data from ResoLink: {e.Message}");
                return null;
            }
        }

        public async Task<ResoLinkComponentResponse> FetchComponent(string id) {
            if (!IsConnected()) return null;
            if (id == null) return null;

            string lowerId = id.ToLowerInvariant();

            try {
                if (cachedMeshComponentData.TryGetValue(lowerId, out ResoLinkComponentResponse cachedResponse)) {
                    return cachedResponse;
                }            
                
                await SendAsync(new GetComponentMessage() { ComponentId = id });
                cachedMeshComponentData[lowerId] = await ReceiveAsyncComponent();;

                return cachedMeshComponentData[lowerId];
            } catch (Exception e) {
                Debug.LogError($"Failed to send and receive data from ResoLink: {e.Message}");
                return null;
            }
        }

        private static readonly HashSet<string> BlacklistedComponents = new HashSet<string> {
            "[FrooxEngine]FrooxEngine.UserRoot",
            "[FrooxEngine]FrooxEngine.Undo.UndoManager",
            "[FrooxEngine]FrooxEngine.SceneInspector",
            "[FrooxEngine]FrooxEngine.SlotGizmo",
            "[FrooxEngine]FrooxEngine.TeleportLocomotion",
            "[FrooxEngine]FrooxEngine.UIX.Canvas",
            "[FrooxEngine]FrooxEngine.HyperlinkDisplayInterface",
            "[FrooxEngine]FrooxEngine.Protoflux.ProtoFluxNodeVisual"
        };

        private ComponentData GetMeshRenderer(SlotData slotData) {
            return slotData?.Components?.FirstOrDefault(c => c.Type == "[FrooxEngine]FrooxEngine.MeshRenderer");
        }

        private Task FetchChildSlotTask(SemaphoreSlim throttler, SlotData slot, ConcurrentDictionary<string, SlotInfo> allSlots, ConcurrentDictionary<string, SlotInfo> slotsWithMeshRenderer, List<string> parentPath) { 
            return Task.Run(async () => {
                await throttler.WaitAsync();
                try {
                    if (!IsConnected()) return;

                    ResoLinkResponse slotData = await FetchSlot(slot.Id, true);

                    if (slotData == null) {
                        Debug.LogWarning($"Failed to fetch data for slot ID: {slot.Id}");
                        return;
                    }

                    ComponentData[] components = slotData.Data?.Components;
                    bool hasBlacklistedComponent = components?.Any(c => BlacklistedComponents.Contains(c.Type)) ?? false;

                    if (hasBlacklistedComponent) return;

                    string slotName = slotData.Data?.Name?.Value ?? "Unknown";
                    string slotId = slotData.Data?.Id ?? "";
                    string pathSegment = $"{slotName} ({slotId})";
                    List<string> currentPath = new List<string>(parentPath) { pathSegment };

                    ComponentData meshRenderer = GetMeshRenderer(slotData.Data);
                    string meshID = meshRenderer?.Members?["Mesh"]?.TargetId;

                    allSlots[slotData.Data.Id] = new SlotInfo {
                        Data = slotData.Data,
                        Path = currentPath.ToArray(),
                        MeshId = meshID
                    };

                    if (meshRenderer != null && !string.IsNullOrEmpty(meshID)) {
                        slotsWithMeshRenderer[slotData.Data.Id] = allSlots[slotData.Data.Id];

                        unityContext?.Post(_ => {
                            CreateUnityHierarchyPart(allSlots[slotData.Data.Id]);
                        }, null);
                    }

                    await FetchAllSlots(slotData, allSlots, slotsWithMeshRenderer, currentPath);
                } finally {
                    throttler.Release();
                }
            });
        }

        private Task FetchAllSlots(ResoLinkResponse rootSlotData, ConcurrentDictionary<string, SlotInfo> allSlots, ConcurrentDictionary<string, SlotInfo> slotsWithMeshRenderer, List<string> parentPath) {
            SlotData[] children = rootSlotData?.Data?.Children;

            if (children == null || children.Length == 0) {
                return null;
            }

            SemaphoreSlim throttler = new SemaphoreSlim(MAX_CONCURRENT_RESOLINK_REQUESTS);
            List<Task> tasks = new List<Task>();

            foreach (SlotData slot in children) {
                if (!IsConnected()) return null;

                tasks.Add(FetchChildSlotTask(throttler, slot, allSlots, slotsWithMeshRenderer, parentPath));
            }

            return Task.WhenAll(tasks);
        }

        public async Task FetchMeshSlots(Action<string, float> progressCallback = null) {
            if (!IsConnected()) return;

            try {
                ResoLinkResponse rootSlotData = await FetchSlot("Root", true);

                if (rootSlotData == null) {
                    Debug.LogWarning("Received empty root slot data from ResoLink");
                    return;
                }

                ConcurrentDictionary<string, SlotInfo> allSlots = new ConcurrentDictionary<string, SlotInfo>();
                ConcurrentDictionary<string, SlotInfo> slotsWithMeshRenderer = new ConcurrentDictionary<string, SlotInfo>();

                string rootName = rootSlotData.Data?.Name?.Value ?? "Root";
                string rootId = rootSlotData.Data?.Id ?? "";
                string rootPathSegment = $"{rootName} ({rootId})";

                ComponentData rootMeshRenderer = GetMeshRenderer(rootSlotData.Data);

                allSlots[rootSlotData.Data.Id] = new SlotInfo {
                    Data = rootSlotData.Data,
                    Path = new string[] { rootPathSegment },
                    MeshId = rootMeshRenderer?.Id
                };

                createdObjects = new Dictionary<string, GameObject>();
                Task slots = FetchAllSlots(rootSlotData, allSlots, slotsWithMeshRenderer, new List<string> { rootPathSegment });
                
                float fakeProgress = 0f;

                while (!slots.IsCompleted) {
                    if (!IsConnected()) return;

                    fakeProgress += (0.95f - fakeProgress) * 0.001f;
                    progressCallback?.Invoke("Retrieving Data from ResoLink...", fakeProgress);
                    await Task.Delay(50);
                }
                
                progressCallback?.Invoke("Retrieving Mesh Renderers", 1f);

                hierarchyData = new HierarchyData {
                    AllSlots = allSlots.Select(kvp => (kvp.Key, kvp.Value)).ToList(),
                    SlotsWithMeshRenderer = slotsWithMeshRenderer.Select(kvp => (kvp.Key, kvp.Value)).ToList()
                };
            } catch (Exception e) {
                Debug.LogError($"Failed to fetch mesh slots from ResoLink: {e.Message}");
            }
        }

        public void CreateUnityHierarchyPart(SlotInfo slotInfo) {         
            try {
                if (!IsConnected()) {
                    Debug.Log("Disconnected during hierarchy creation");
                    return;
                }
                    
                if (slotInfo == null || slotInfo.Path == null || slotInfo.Path.Length == 0) return;

                GameObject currentObject = null;
                string currentPath = "";

                for (int i = 0; i < slotInfo.Path.Length; i++) {
                    if (!IsConnected()) return;

                    string pathSegment = slotInfo.Path[i];
                    currentPath = i == 0 ? pathSegment : currentPath + "/" + pathSegment;

                    if (!createdObjects.ContainsKey(currentPath)) {
                        GameObject newObject = new GameObject(pathSegment);
                            
                        if (currentObject != null) {
                            newObject.transform.SetParent(currentObject.transform, false);
                        }
                            
                        createdObjects[currentPath] = newObject;
                        currentObject = newObject;
                    } else {
                        currentObject = createdObjects[currentPath];
                    }
                }
            } catch (Exception e) {
                Debug.LogError($"Failed to create Unity Hierarchy: {e.Message}");
            }
        }

        public Task BuildLookupTables(Action<string, float> progressCallback = null) {
            try {
                progressCallback?.Invoke("Building lookup tables...", 0f);
                slotDataLookup = new Dictionary<string, SlotData>();

                int counter = 0;
                foreach ((string, SlotInfo) slot in hierarchyData.AllSlots) {
                    if (!IsConnected()) return null;

                    SlotInfo slotInfo = slot.Item2;
                    if (slotInfo != null && slotInfo.Path != null && slotInfo.Data != null) {
                        string fullPath = string.Join("/", slotInfo.Path);
                        slotDataLookup[fullPath] = slotInfo.Data;
                    }
                    counter++;
                    progressCallback?.Invoke("Building lookup tables...", (counter / hierarchyData.AllSlots.Count));
                }
            } catch (Exception e) { 
                Debug.LogError($"Error during lookup table build: {e.Message}\n{e.StackTrace}");
            }

            return Task.CompletedTask;
        }

        public Task ApplyTRSToObjects(Action<string, float> progressCallback = null) {
            try {
                progressCallback?.Invoke("Applying TRS...", 0f);

                List<string> sortedPaths = createdObjects.Keys.OrderBy(path => path.Count(part => part == '/')).ToList();
                int transformProcessed = 0;
                int totalTransforms = sortedPaths.Count;

                foreach (string path in sortedPaths) {
                    if (!IsConnected()) return null;

                    GameObject obj = createdObjects[path];
                    if (slotDataLookup.TryGetValue(path, out SlotData slotData)) {
                        if (slotData.Position?.Value != null) {
                            Vector3 value = slotData.Position.Value;
                            obj.transform.localPosition = new Vector3(value.x, value.y, value.z);
                        }
                        if (slotData.Rotation?.Value != null) {
                            Vector4 value = slotData.Rotation.Value;
                            obj.transform.localRotation = new Quaternion(value.x, value.y, value.z, value.w);
                        }
                        if (slotData.Scale?.Value != null) {
                            Vector3 value = slotData.Scale.Value;
                            obj.transform.localScale = new Vector3(value.x, value.y, value.z);
                        }
                    }
                    transformProcessed++;
                    if (transformProcessed % 20 == 0 || transformProcessed == totalTransforms) {
                        float progress = transformProcessed / (float)totalTransforms;
                        progressCallback?.Invoke($"Applying TRS... ({transformProcessed}/{totalTransforms})", progress);
                    }
                }
            } catch (Exception e) {
                Debug.LogError($"Error during TRS apply: {e.Message}\n{e.StackTrace}");
                return null;
            }
            return Task.CompletedTask;;
        }

        public async Task DownloadAndApplyMeshes(Action<string, float, GameObject> progressCallback = null) {
            List<(string, SlotInfo)> slotList = hierarchyData.SlotsWithMeshRenderer;

            for (int i = 0; i < slotList.Count; i += MAX_CONCURRENT_MESH_DOWNLOADS) {
                if (!IsConnected()) return;

                GameObject lastSuccessfulObject = null;
                int batchSize = Math.Min(MAX_CONCURRENT_MESH_DOWNLOADS, slotList.Count - i);
                IEnumerable<(string, SlotInfo)> batch = slotList.Skip(i).Take(batchSize);

                await Task.WhenAll(batch.Select(async item => {
                    try {
                        SlotInfo slotInfo = item.Item2;

                        if (!IsConnected()) return;
                        if (slotInfo == null || slotInfo.Path == null || slotInfo.Path.Length == 0 || slotInfo.MeshId == null) return;

                        GameObject targetObject = createdObjects[string.Join("/", slotInfo.Path)];
                        ResoLinkComponentResponse component = await FetchComponent(slotInfo.MeshId);

                        if (component?.Data?.Type != "[FrooxEngine]FrooxEngine.StaticMesh" || component?.Data?.Members?["URL"]?.Value == null) return;

                        Mesh mesh = await AcquireMesh(component?.Data?.Members?["URL"]?.Value);

                        if (mesh == null) return;

                        MeshXConverter.ApplyMeshToGameObject(mesh, targetObject);
                        lastSuccessfulObject = targetObject;
                    } catch (Exception e) {
                        Debug.LogError($"Error downloading/applying mesh for slot {item.Item1}: {e.Message}\n{e.StackTrace}");
                    }
                }));

                progressCallback?.Invoke($"Downloading meshes... ", i / (float)slotList.Count, lastSuccessfulObject);
            }
        }

        private async Task<Mesh> AcquireMesh(string meshId) { 
            try {
                meshId = meshId.Replace(".meshx", "").Replace("resdb:///", "").Replace(".", "");
        
                bool isLocal = meshId.StartsWith("local://");

                if (isLocal) {
                    meshId = meshId.Replace("local://", "").Split("/")[1]; 
                }

                Mesh meshData = MeshXCache.Instance.GetFromMeshDataCache(meshId);
                if (meshData != null) return meshData;

                MeshXData meshXData = await (isLocal ? MeshXHelper.Instance.DownloadLocalMeshX(meshId) : MeshXHelper.Instance.DownloadMeshX(meshId));
                if (meshXData == null) return null;
                
                Mesh mesh = MeshXConverter.ConvertToUnityMesh(meshXData);
                if (mesh == null) return null;
                
                MeshXCache.Instance.AddToMeshDataCache(meshId, mesh);
                return mesh;
            } catch (Exception e) {
                Debug.LogError($"Error downloading and converting MeshX {meshId}: {e.Message}");
                return null;
            }
        }
    }
}
