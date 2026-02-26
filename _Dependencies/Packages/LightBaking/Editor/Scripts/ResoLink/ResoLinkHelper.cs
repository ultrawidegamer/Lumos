using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ResoMeshXParsing;
using ResoniteLink;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace LightBakingResoLink {
    public class ResoLinkHelper {
        private static ResoLinkHelper instance;
        public LinkInterface linkInterface;
        private static readonly object lockObj = new object();
        private const int MAX_CONCURRENT_RESOLINK_REQUESTS = 10;
        private const int MAX_CONCURRENT_MESH_DOWNLOADS = 10;
        public HierarchyData hierarchyData = null;
        private ConcurrentDictionary<string, SlotData> slotDataLookup = new ConcurrentDictionary<string, SlotData>();
        private ConcurrentDictionary<string, GameObject> createdObjects = new ConcurrentDictionary<string, GameObject>();
        private ConcurrentDictionary<string, ComponentData> cachedMeshComponentData = new ConcurrentDictionary<string, ComponentData>();
        private CancellationTokenSource cancellation = new CancellationTokenSource();
        private (List<Member>, List<Member>) lumosConfig = (null, null);
        private SlotData lumosConfigSlot = null;
        private List<Member> textureElementsToSend = null;
        private List<Member> colorElementsToSend = null;
        public bool respectSlotActiveState = true;
        public bool respectMeshRendererActiveState = true;

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

        public async Task<bool> ConnectAsync(string url) {
            if (string.IsNullOrEmpty(url)) {
                return false;
            }

            try {
                linkInterface = new LinkInterface();
                await linkInterface.Connect(new Uri(url), cancellation.Token);
                return true;
            } catch (Exception e) {
                Debug.LogError($"Failed to connect to ResoLink: {e.Message}");
                return false;
            }
        }

        public void Disconnect() {
            try {
                linkInterface.Dispose();
            } catch (Exception e) {
                Debug.LogError($"Failed to disconnect from ResoLink: {e.Message}");
            }
        }

        public bool IsConnected() {
            return linkInterface?.IsConnected ?? false;
        }

        public async Task<SlotData> FetchSlot(string id, bool withComponents = false) {
            if (!IsConnected()) return null;
            if (id == null) return null;

            try {
                return await linkInterface.GetSlotData(new GetSlot() { SlotID = id, IncludeComponentData = withComponents });
            } catch (Exception e) {
                Debug.LogError($"Failed to send and receive data from ResoLink: {e.Message}");
                return null;
            }
        }

        public async Task<ComponentData> FetchComponent(string id) {
            if (!IsConnected()) return null;
            if (id == null) return null;

            string lowerId = id.ToLowerInvariant();

            if (cachedMeshComponentData.TryGetValue(lowerId, out var cached)) {
                return cached;
            }

            ComponentData component = await linkInterface.GetComponentData(new GetComponent() { ComponentID = id });
            cachedMeshComponentData.TryAdd(lowerId, component);
            return component;
        }

        private async Task<string> AddSlotToResolink(string name, string parentId) {
            if (!IsConnected()) return null;
            try {
                NewEntityId newId = await linkInterface.AddSlot(new AddSlot() {
                    Data = new Slot {
                        Parent = new Reference { TargetID = parentId },
                        Name = new Field_string { Value = name },
                        Position = new Field_float3 { Value = new float3 { x = 0f, y = 0f, z = 0f } },
                        Rotation = new Field_floatQ { Value = new floatQ { x = 0f, y = 0f, z = 0f, w = 0f } },
                        Scale = new Field_float3 { Value = new float3 { x = 1f, y = 1f, z = 1f } }
                    }
                });
                
                return newId?.EntityId;
            } catch (Exception e) {
                Debug.LogError($"Failed to add slot to ResoLink: {e.Message}");
                return null;
            }
        }

        private async Task<string> AddComponentToResolinkSlot(string slotId, ResoniteLink.Component component) {
            if (!IsConnected()) return null;
            try {
                NewEntityId newId = await linkInterface.AddComponent(new AddComponent() {
                    ContainerSlotId = slotId,
                    Data = component
                });

                return newId.EntityId;
            } catch (Exception e) {
                Debug.LogError($"Failed to add slot to ResoLink: {e.Message}");
                return null;
            }
        }

        private async Task<string> AddMeshToResolinkSlot(string slotId, string meshUri) {
            if (!IsConnected()) return null;
            try {
                return await AddComponentToResolinkSlot(slotId, new ResoniteLink.Component {
                    ComponentType = "[FrooxEngine]FrooxEngine.StaticMesh",
                    Members = new Dictionary<string, Member> {
                        { "URL", new Field_Uri { Value = new Uri(meshUri) } }
                    }
                });
            } catch (Exception e) {
                Debug.LogError($"Failed to add mesh to slot: {e.Message}");
                return null;
            }
        }

        private async Task<string> AddTextureToResolinkSlot(string slotId, string textureUri) {
            if (!IsConnected()) return null;
            try {
                return await AddComponentToResolinkSlot(slotId, new ResoniteLink.Component {
                    ComponentType = "[FrooxEngine]FrooxEngine.StaticTexture2D",
                    Members = new Dictionary<string, Member> {
                        { "URL", new Field_Uri { Value = new Uri(textureUri) } },
                        { "FilterMode", new Field_Nullable_Enum { Value = "Bilinear" } },
                        { "AnisotropicLevel", new Field_Nullable_int { Value = 0 } },
                        { "Uncompressed", new Field_bool { Value = false } },
                        { "ForceExactVariant", new Field_bool  { Value = true } },
                        { "PreferredFormat", new Field_Nullable_Enum { Value = "BC6H_LZMA" } },
                        { "PreferredProfile", new Field_Nullable_Enum { Value = "Linear" } },
                        { "WrapModeU", new Field_Enum { Value = "Clamp" } },
                        { "WrapModeV", new Field_Enum { Value = "Clamp" } },
                        { "CrunchCompressed", new Field_bool { Value = false } },
                        { "MinSize", new Field_Nullable_int { Value = 8192 } },
                        { "MipMaps", new Field_bool { Value = false } }
                    }
                });
            } catch (Exception e) {
                Debug.LogError($"Failed to add texture to slot: {e.Message}");
                return null;
            }
        }

        private async Task<string> AddMaterialToResolinkSlot(string slotId, string textureId) {
            if (!IsConnected()) return null;
            try {
                return await AddComponentToResolinkSlot(slotId, new ResoniteLink.Component {
                    ComponentType = "[FrooxEngine]FrooxEngine.UnlitMaterial",
                    Members = new Dictionary<string, Member> {
                        { "Texture", new Reference { TargetID = textureId } },
                        { "BlendMode",  new Field_Enum { Value = "Multiply" } },
                        { "OffsetUnits", new Field_float { Value = -1f } },
                        { "Sidedness", new Field_Enum { Value = "Front" } },
                        { "RenderQueue", new Field_int { Value = 2000 } }
                    }
                });
            } catch (Exception e) {
                Debug.LogError($"Failed to add material to slot: {e.Message}");
                return null;
            }
        }

        private async Task<string> AddMeshRendererToResolinkSlot(string slotId, string meshId, string materialId) {
            if (!IsConnected()) return null;
            try {
                return await AddComponentToResolinkSlot(slotId, new ResoniteLink.Component {
                    ComponentType = "[FrooxEngine]FrooxEngine.MeshRenderer",
                    Members = new Dictionary<string, Member> {
                        { "Mesh", new Reference { TargetID = meshId } },
                        { "Materials",  new SyncList { Elements = new List<Member> { new Reference { TargetID = materialId } } } },
                        { "ShadowCastMode", new Field_Enum { Value = "Off" } },
                        { "MotionVectorMode", new Field_Enum { Value = "NoMotion" } },
                    }
                });
            } catch (Exception e) {
                Debug.LogError($"Failed to add material to slot: {e.Message}");
                return null;
            }
        }

        private async Task<string> AddMultiDriverToResolinkSlot(string slotId, string subType, Dictionary<string, Member> members) {
            if (!IsConnected()) return null;
            try {
                return await AddComponentToResolinkSlot(slotId, new ResoniteLink.Component {
                    ComponentType = $"[FrooxEngine]FrooxEngine.ValueMultiDriver<{subType}>",
                    Members = members
                });
            } catch (Exception e) {
                Debug.LogError($"Failed to add destroy proxy to slot: {e.Message}");
                return null;
            }
        }

        private async Task<string> AddBooleanValueDriverToResolinkSlot(string slotId, string subType, Dictionary<string, Member> members) {
            if (!IsConnected()) return null;
            try {
                return await AddComponentToResolinkSlot(slotId, new ResoniteLink.Component {
                    ComponentType = $"[FrooxEngine]FrooxEngine.BooleanValueDriver<{subType}>",
                    Members = members
                });
            } catch (Exception e) {
                Debug.LogError($"Failed to add destroy proxy to slot: {e.Message}");
                return null;
            }
        }

        private async Task<string> AddDestroyProxyToResolinkSlot(string slotId, string targetId) {
            if (!IsConnected()) return null;
            try {
                return await AddComponentToResolinkSlot(slotId, new ResoniteLink.Component {
                    ComponentType = "[FrooxEngine]FrooxEngine.DestroyProxy",
                    Members = new Dictionary<string, Member> {
                        { "DestroyTarget", new Reference { TargetID = targetId } }
                    }
                });
            } catch (Exception e) {
                Debug.LogError($"Failed to add destroy proxy to slot: {e.Message}");
                return null;
            }
        }

        private static readonly HashSet<string> BlacklistedComponentsSlot = new HashSet<string> {
            "[FrooxEngine]FrooxEngine.UserRoot",
            "[FrooxEngine]FrooxEngine.Undo.UndoManager",
            "[FrooxEngine]FrooxEngine.SceneInspector",
            "[FrooxEngine]FrooxEngine.SlotGizmo",
            "[FrooxEngine]FrooxEngine.TeleportLocomotion",
            "[FrooxEngine]FrooxEngine.UIX.Canvas",
            "[FrooxEngine]FrooxEngine.HyperlinkDisplayInterface",
            "[FrooxEngine]FrooxEngine.VideoPlayerInterface",
            "[FrooxEngine]FrooxEngine.Protoflux.ProtoFluxNodeVisual"
        };

        private static readonly HashSet<string> BlacklistedComponentsChildren = new HashSet<string> {
            "[FrooxEngine]FrooxEngine.Light"
        };

        private ResoniteLink.Component GetMeshRenderer(SlotData slotData) {
            return slotData.Data.Components.FirstOrDefault(c => c.ComponentType == "[FrooxEngine]FrooxEngine.MeshRenderer");
        }

        private ResoniteLink.Component GetLightComponent(SlotData slotData) {
            return slotData.Data.Components.FirstOrDefault(c => c.ComponentType == "[FrooxEngine]FrooxEngine.Light");
        }

        private Task FetchChildSlotTask(
            SemaphoreSlim throttler,
            Slot slot,
            ConcurrentDictionary<string, SlotInfo> allSlots,
            ConcurrentDictionary<string, SlotInfo> slotsWithMeshRenderer,
            List<string> parentPath) {
            return Task.Run(async () => {
                await throttler.WaitAsync();
                try {
                    if (!IsConnected()) return;                    
                    if (lumosConfig.Item1?.Any(s => (s as Reference)?.TargetID == slot.ID) ?? false) return;

                    SlotData slotData = await FetchSlot(slot.ID, true);

                    if (slotData == null) return;
                    if (slotData.Data == null) return;

                    List<ResoniteLink.Component> components = slotData.Data.Components;

                    bool hasBlacklistedComponent = components?.Any(c => BlacklistedComponentsSlot.Contains(c.ComponentType)) ?? false;
                    bool hasBlacklistedComponentsChildren = components?.Any(c => BlacklistedComponentsChildren.Contains(c.ComponentType)) ?? false;
                    
                    if (hasBlacklistedComponent) return;

                    string slotName = slotData.Data?.Name?.Value ?? "Unknown";
                    string slotId = slotData.Data.ID ?? "";
                    string pathSegment = $"{slotName} ({slotId})";
                    List<string> currentPath = new List<string>(parentPath) { pathSegment };

                    ResoniteLink.Component meshRenderer = GetMeshRenderer(slotData);
                    ResoniteLink.Component light = GetLightComponent(slotData);

                    Member meshMember = null;
                    meshRenderer?.Members?.TryGetValue("Mesh", out meshMember);
                    string meshID = (meshMember as Reference)?.TargetID;

                    allSlots[slotData.Data.ID] = new SlotInfo {
                        Data = slotData,
                        Path = currentPath.ToArray(),
                        MeshId = meshID,
                        Light = light
                    };

                    bool hasLight = light != null && (lumosConfig.Item2?.Any(l => (l as Reference).TargetID == light.ID) ?? false);
                    bool hasMeshRenderer = meshRenderer != null && !string.IsNullOrEmpty(meshID);

                    if (hasLight) {
                        allSlots[slotData.Data.ID].Light = light;
                    }

                    if (hasMeshRenderer || hasLight) {
                        slotsWithMeshRenderer[slotData.Data.ID] = allSlots[slotData.Data.ID];

                        TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();

                        TaskQueue.Enqueue(() => {
                            CreateUnityHierarchyPart(allSlots[slotData.Data.ID]);
                            tcs.SetResult(true);
                        });

                        await tcs.Task;
                    }

                    if (hasBlacklistedComponentsChildren) return;

                    await FetchAllSlots(slotData, allSlots, slotsWithMeshRenderer, currentPath);
                } finally {
                    throttler.Release();
                }
            });
        }

        private Task FetchAllSlots(
            SlotData rootSlotData,
            ConcurrentDictionary<string, SlotInfo> allSlots,
            ConcurrentDictionary<string, SlotInfo> slotsWithMeshRenderer,
            List<string> parentPath) {
            if (rootSlotData == null) return null;
            if (rootSlotData.Data == null) return null;

            List<Slot> children = rootSlotData.Data.Children;

            if (children == null)  return null;
            if (children.Count == 0) return null;

            SemaphoreSlim throttler = new SemaphoreSlim(MAX_CONCURRENT_RESOLINK_REQUESTS);
            List<Task> tasks = new List<Task>();

            foreach (Slot slot in children) {
                if (!IsConnected()) return null;
                tasks.Add(FetchChildSlotTask(throttler, slot, allSlots, slotsWithMeshRenderer, parentPath));
            }

            return Task.WhenAll(tasks);
        }

        private async Task FetchConfig(SlotData root) {
            if (!IsConnected()) return;
            try {
                if (root == null) return;

                Slot config = root.Data.Children.FirstOrDefault(c => c.Name.Value == "LumosConfig");
                string configId = config?.ID;

                if (config == null || configId == null) {
                    configId = await AddSlotToResolink("LumosConfig", "Root"); 
                }
                
                lumosConfigSlot = await FetchSlot(configId, true);
                if (lumosConfigSlot == null || lumosConfigSlot?.Data == null) return;

                Member slotReferences = null;
                ResoniteLink.Component slotList = lumosConfigSlot?.Data?.Components.FirstOrDefault(c => c.ComponentType == "[FrooxEngine]FrooxEngine.ReferenceList<[FrooxEngine]FrooxEngine.Slot>");
                slotList?.Members?.TryGetValue("References", out slotReferences);

                Member lightReferences = null;
                ResoniteLink.Component lightList = lumosConfigSlot?.Data?.Components.FirstOrDefault(c => c.ComponentType == "[FrooxEngine]FrooxEngine.ReferenceList<[FrooxEngine]FrooxEngine.Light>");
                lightList?.Members?.TryGetValue("References", out lightReferences);

                List<Member> slotElements = (slotReferences as SyncList)?.Elements;
                List<Member> lightElements = (lightReferences as SyncList)?.Elements;

                lumosConfig = (slotElements, lightElements);
            } catch (Exception e) {
                Debug.LogError($"Failed to fetch config from ResoLink: {e.Message}");
            }
        }

        public async Task FetchSlots(Action<string, float> progressCallback = null) {
            if (!IsConnected()) return;

            try {
                SlotData rootSlotData = await FetchSlot("Root", true);

                if (rootSlotData == null) return;
                if (rootSlotData.Data == null) return;
                
                await FetchConfig(rootSlotData);

                ConcurrentDictionary<string, SlotInfo> allSlots = new ConcurrentDictionary<string, SlotInfo>();
                ConcurrentDictionary<string, SlotInfo> slotsWithMeshRenderer = new ConcurrentDictionary<string, SlotInfo>();

                string rootName = rootSlotData.Data?.Name?.Value ?? "Root";
                string rootId = rootSlotData.Data.ID ?? "";
                string rootPathSegment = $"{rootName} ({rootId})";

                ResoniteLink.Component rootMeshRenderer = GetMeshRenderer(rootSlotData);

                allSlots[rootSlotData.Data.ID] = new SlotInfo {
                    Data = rootSlotData,
                    Path = new string[] { rootPathSegment },
                    MeshId = rootMeshRenderer?.ID
                };

                createdObjects = new ConcurrentDictionary<string, GameObject>();
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
                if (!IsConnected()) return;
                    
                if (slotInfo == null || slotInfo.Path == null || slotInfo.Path.Length == 0) return;

                GameObject currentObject = null;
                string currentPath = "";

                for (int i = 0; i < slotInfo.Path.Length; i++) {
                    if (!IsConnected()) return;

                    string pathSegment = slotInfo.Path[i];
                    currentPath = i == 0 ? pathSegment : currentPath + "/" + pathSegment;

                    currentObject = createdObjects.GetOrAdd(currentPath, _ => {
                        GameObject newObject = new GameObject(pathSegment);
                        if (currentObject != null) {
                            newObject.transform.SetParent(currentObject.transform, false);

                            AddLightToScene(newObject, slotInfo.Light);
                        }
                        return newObject;
                    });
                }
            } catch (Exception e) {
                Debug.LogError($"Failed to create Unity Hierarchy: {e.Message}");
            }
        }

        public Task BuildLookupTables(Action<string, float> progressCallback = null) {
            try {
                progressCallback?.Invoke("Building lookup tables...", 0f);
                slotDataLookup = new ConcurrentDictionary<string, SlotData>();

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
                        bool shouldBeActive = respectSlotActiveState ? (slotData.Data.IsActive?.Value ?? true) : true;
                        obj.SetActive(shouldBeActive);

                        if (slotData.Data.Position?.Value != null) {
                            float3 value = slotData.Data.Position.Value;
                            obj.transform.localPosition = new Vector3(value.x, value.y, value.z);
                        }
                        if (slotData.Data.Rotation?.Value != null) {
                            floatQ value = slotData.Data.Rotation.Value;
                            obj.transform.localRotation = new Quaternion(value.x, value.y, value.z, value.w);
                        }
                        if (slotData.Data.Scale?.Value != null) {
                            float3 value = slotData.Data.Scale.Value;
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

            progressCallback?.Invoke($"Downloading meshes... ", 0f, null);

            for (int i = 0; i < slotList.Count; i += MAX_CONCURRENT_MESH_DOWNLOADS) {
                if (!IsConnected()) return;

                GameObject lastSuccessfulObject = null;
                int batchSize = Math.Min(MAX_CONCURRENT_MESH_DOWNLOADS, slotList.Count - i);
                IEnumerable<(string, SlotInfo)> batch = slotList.Skip(i).Take(batchSize);

                await Task.WhenAll(batch.Select(async item => {
                    try {
                        SlotInfo slotInfo = item.Item2;
                        if (!IsConnected()) return;
                        if (slotInfo == null || slotInfo.Path == null || slotInfo.Path.Length == 0 || string.IsNullOrEmpty(slotInfo.MeshId)) return;

                        string pathKey = string.Join("/", slotInfo.Path);
                        GameObject targetObject = null;

                        createdObjects.TryGetValue(pathKey, out targetObject);
                        if (targetObject == null) return;

                        ComponentData component = await FetchComponent(slotInfo.MeshId);
                        if (component == null || component.Data == null) return;

                        Member urlMember = null;
                        component.Data.Members.TryGetValue("URL", out urlMember);
                        string meshUri = (urlMember as Field_Uri)?.Value?.ToString();

                        ResoniteLink.Component meshRenderer = GetMeshRenderer(slotInfo.Data);

                        meshRenderer.Members.TryGetValue("Enabled", out Member enabled);
                        bool shouldBeActive = respectMeshRendererActiveState ? ((enabled as Field_bool)?.Value ?? true) : true;

                        if (urlMember == null || meshUri == null) return;

                        Mesh mesh = await AcquireMesh(meshUri);
                        if (mesh == null) return;

                        MeshRenderer renderer = MeshXConverter.ApplyMeshToGameObject(mesh, targetObject, shouldBeActive);

                        TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();

                        TaskQueue.Enqueue(() => {
                            GameObjectUtility.SetStaticEditorFlags(
                                targetObject,
                                GameObjectUtility.GetStaticEditorFlags(targetObject) | StaticEditorFlags.ContributeGI
                            );
                            renderer.receiveGI = ReceiveGI.Lightmaps;

                            Unwrapping.GenerateSecondaryUVSet(mesh);
                            
                            tcs.SetResult(true);
                        });

                        await tcs.Task;

                        lastSuccessfulObject = targetObject;
                    } catch (Exception e) {
                        Debug.LogError($"Error downloading/applying mesh for slot {item.Item1}: {e.Message}\n{e.StackTrace}");
                    }
                }));

                progressCallback?.Invoke($"Downloading meshes... ", i / (float)slotList.Count, lastSuccessfulObject);
            }
        }

        public void AddLightToScene(GameObject slot, ResoniteLink.Component light) {
            if (!IsConnected()) return;
            if (slot == null || light == null) return;

            Light lightObj = slot.AddComponent<Light>();

            light.Members.TryGetValue("LightType", out Member lightType);
            light.Members.TryGetValue("Intensity", out Member intensity);
            light.Members.TryGetValue("Color", out Member color);
            light.Members.TryGetValue("ShadowType", out Member shadowType);
            light.Members.TryGetValue("ShadowStrength", out Member shadowStrength);
            light.Members.TryGetValue("ShadowNearPlane", out Member shadowNearPlane);
            light.Members.TryGetValue("ShadowBias", out Member shadowBias);
            light.Members.TryGetValue("ShadowNormalBias", out Member shadowNormalBias);
            light.Members.TryGetValue("Range", out Member range);
            light.Members.TryGetValue("SpotAngle", out Member spotAngle);

            lightObj.type = new Dictionary<string, LightType> {
                { "Directional", LightType.Directional },
                { "Point", LightType.Point},
                { "Spot", LightType.Spot}
            }[(lightType as Field_Enum).Value];

            lightObj.intensity = (intensity as Field_float)?.Value ?? 1f;

            lightObj.color = new Color(
                ((color as Field_colorX)?.Value.r ?? 1f),
                ((color as Field_colorX)?.Value.g ?? 1f),
                ((color as Field_colorX)?.Value.b ?? 1f),
                ((color as Field_colorX)?.Value.a ?? 1f)
            );

            lightObj.shadows = new Dictionary<string, LightShadows> {
                { "None", LightShadows.None },
                { "Soft", LightShadows.Soft },
                { "Hard", LightShadows.Hard }
            }[(shadowType as Field_Enum).Value];

            lightObj.shadowStrength = (shadowStrength as Field_float)?.Value ?? 1f;
            lightObj.shadowNearPlane = (shadowNearPlane as Field_float)?.Value ?? 0.2f;
            lightObj.shadowBias = (shadowBias as Field_float)?.Value ?? 0.05f;
            lightObj.shadowNormalBias = (shadowNormalBias as Field_float)?.Value ?? 0.4f;
            lightObj.range = (range as Field_float)?.Value ?? 10f;
            
            if (lightObj.type == LightType.Spot) {
                lightObj.spotAngle = (spotAngle as Field_float)?.Value ?? 30f;
            }

            lightObj.lightmapBakeType = LightmapBakeType.Baked;
        }

        private string GetResoLinkSlotIdFromName(string name) {
            string[] slotNameArray = name.Split("(");
            return slotNameArray[slotNameArray.Length-1].Replace(")", "");
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

                MeshXCache.Instance.AddToMeshDataCache(meshId, mesh);
                return mesh;
            } catch (Exception e) {
                Debug.LogError($"Error downloading and converting MeshX {meshId}: {e.Message}");
                return null;
            }
        }

        public async Task<string> SendUnityMeshToResoLink(Mesh mesh) {
            if (!IsConnected() || mesh == null) {
                Debug.LogWarning("Not connected or mesh is null.");
                return null;
            }

            try {
                ImportMeshRawData importData = new ImportMeshRawData {
                    VertexCount = mesh.vertexCount,
                    HasNormals = mesh.normals != null && mesh.normals.Length == mesh.vertexCount,
                    HasTangents = mesh.tangents != null && mesh.tangents.Length == mesh.vertexCount,
                    HasColors = mesh.colors != null && mesh.colors.Length == mesh.vertexCount,
                    UV_Channel_Dimensions = new List<int>(),
                    Submeshes = new List<SubmeshRawData>()
                };

                if (mesh.uv != null && mesh.uv.Length == mesh.vertexCount) importData.UV_Channel_Dimensions.Add(2);
                if (mesh.uv2 != null && mesh.uv2.Length == mesh.vertexCount) importData.UV_Channel_Dimensions.Add(2);
                if (mesh.uv3 != null && mesh.uv3.Length == mesh.vertexCount) importData.UV_Channel_Dimensions.Add(2);
                if (mesh.uv4 != null && mesh.uv4.Length == mesh.vertexCount) importData.UV_Channel_Dimensions.Add(2);

                List<int> submeshIndexes = new List<int>();
                for (int s = 0; s < mesh.subMeshCount; s++) {
                    SubMeshDescriptor submeshDesc = mesh.GetSubMesh(s);

                    switch (submeshDesc.topology) {
                        case MeshTopology.Triangles:
                            TriangleSubmeshRawData triangleSubmesh = new TriangleSubmeshRawData {
                                TriangleCount = submeshDesc.indexCount / 3
                            };
                            importData.Submeshes.Add(triangleSubmesh);
                            submeshIndexes.Add(s);
                            break;
                        case MeshTopology.Points:
                            PointSubmeshRawData pointSubmesh = new PointSubmeshRawData {
                                PointCount = submeshDesc.indexCount
                            };
                            importData.Submeshes.Add(pointSubmesh);
                            submeshIndexes.Add(s);
                            break;
                        default:
                            Debug.LogWarning($"Unsupported submesh topology: {submeshDesc.topology}");
                            break;
                    }
                }

                if (importData.Submeshes.Count == 0) {
                    Debug.LogError("Mesh must have at least one supported submesh (triangles or points).");
                    return null;
                }

                importData.AllocateBuffer();

                FillImportMeshRawDataBuffers(importData, mesh);

                for (int s = 0; s < importData.Submeshes.Count; s++) {
                    SubmeshRawData submesh = importData.Submeshes[s];
                    int[] indices = mesh.GetIndices(submeshIndexes[s]);
                    for (int i = 0; i < indices.Length; i++) {
                        submesh.Indices[i] = indices[i];
                    }
                }

                if (linkInterface == null) return null;
                if (importData == null) return null;

                AssetData data = await linkInterface.ImportMesh(importData);

                return data?.AssetURL?.ToString();
            } catch (Exception e) {
                Debug.LogError($"Exception: {e.Message}\n{e.StackTrace}");
                return null;
            }
        }

        private void FillImportMeshRawDataBuffers(ImportMeshRawData importData, Mesh mesh) {
            var positions = importData.Positions;
            var meshVertices = mesh.vertices;

            for (int i = 0; i < mesh.vertexCount; i++) {
                positions[i] = new float3 { x = meshVertices[i].x, y = meshVertices[i].y, z = meshVertices[i].z };
            }

            if (importData.HasNormals) {
                var normals = importData.Normals;
                var meshNormals = mesh.normals;
                for (int i = 0; i < mesh.vertexCount; i++) {
                    normals[i] = new float3 { x = meshNormals[i].x, y = meshNormals[i].y, z = meshNormals[i].z };
                }
            }

            if (importData.HasTangents) {
                var tangents = importData.Tangents;
                var meshTangents = mesh.tangents;
                for (int i = 0; i < mesh.vertexCount; i++) { 
                    tangents[i] = new float4 {
                        x = meshTangents[i].x,
                        y = meshTangents[i].y,
                        z = meshTangents[i].z,
                        w = meshTangents[i].w
                    };
                }
            }

            if (importData.HasColors) {
                var colors = importData.Colors;
                var meshColors = mesh.colors;
                for (int i = 0; i < mesh.vertexCount; i++) {
                    colors[i] = new color {
                        r = meshColors[i].r,
                        g = meshColors[i].g,
                        b = meshColors[i].b,
                        a = meshColors[i].a
                    };
                }
            }

            int uvIndex = 0;
            Vector2[][] meshUVs = { mesh.uv, mesh.uv2, mesh.uv3, mesh.uv4 };

            foreach (Vector2[] uvSet in meshUVs) {
                if (uvSet == null || uvSet.Length != mesh.vertexCount) continue;
                
                var uvs = importData.AccessUV_2D(uvIndex++);
                for (int i = 0; i < mesh.vertexCount; i++) {
                    uvs[i] = new float2 { x = uvSet[i].x, y = uvSet[i].y };
                }                
            }
        }

        private void SetTextureReadable(Texture2D texture) {
            if (texture == null || texture.isReadable) return;

            string assetPath = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(assetPath)) return;

            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            
            if (importer == null || importer.isReadable) return;

            importer.isReadable = true;
            importer.SaveAndReimport();
            
        }

        private async Task<string> SendHDRTextureToResoLink(Texture2D texture) {
            if (!IsConnected()) return null;

            ImportTexture2DRawDataHDR import = new ImportTexture2DRawDataHDR {
                Width = texture.width,
                Height = texture.height
            };

            FillImportHDRTexture2DRawData(import, texture);
            AssetData assetData = await linkInterface.ImportTexture(import);

            return assetData?.AssetURL?.ToString();
        }

        private async Task<string> SendSDRTextureToResoLink(Texture2D texture) {
            if (!IsConnected()) return null;

            ImportTexture2DRawData import = new ImportTexture2DRawData {
                Width = texture.width,
                Height = texture.height
            };

            FillImportSDRTexture2DRawData(import, texture);
            AssetData assetData = await linkInterface.ImportTexture(import);

            return assetData?.AssetURL?.ToString();
        }

        private void FillImportHDRTexture2DRawData(ImportTexture2DRawDataHDR import, Texture2D texture) {
            Span2D<color> data = import.AccessRawData();
            Color[] pixels = texture.GetPixels(0);
            int width = import.Width;
            int height = import.Height;

            for (int y = 0; y < height; y++) {
                int flippedY = height - 1 - y;
                for (int x = 0; x < width; x++) {
                    int idx = x + flippedY * width;
                    Color px = pixels[idx];
                    data[x, y] = new color {
                        r = px.r,
                        g = px.g,
                        b = px.b,
                        a = px.a
                    };
                }
            }
        }

        private void FillImportSDRTexture2DRawData(ImportTexture2DRawData import, Texture2D texture) {
            Span2D<color32> data = import.AccessRawData();
            Color[] pixels = texture.GetPixels(0);
            int width = import.Width;
            int height = import.Height;
            
            for (int y = 0; y < height; y++) {
                int flippedY = height - 1 - y;
                for (int x = 0; x < width; x++) {
                    int idx = x + flippedY * width;
                    Color32 px = pixels[idx];
                    data[x, y] = new color32 {
                        r = px.r,
                        g = px.g,
                        b = px.b,
                        a = px.a
                    };
                }
            }
        }

        public async Task<string> SendTextureToResoLink(Texture2D texture) {
            if (!IsConnected()) return null;
            if (texture == null) return null;
            SetTextureReadable(texture);

            if (GraphicsFormatUtility.IsHDRFormat(texture.graphicsFormat)) {
                return await SendHDRTextureToResoLink(texture);
            } else {
                return await SendSDRTextureToResoLink(texture);
            }
        }

        public async Task<String> SendTextureToResoLinkViaObject(GameObject textureObject) {
            if (!IsConnected()) return null;
            if (textureObject == null) return null;
            Renderer renderer = textureObject.GetComponent<Renderer>();

            if (renderer == null) return null;
            if (renderer.sharedMaterials.Length == 0) return null;
            
            Material material = renderer.sharedMaterials[0];
            Texture2D albedoTexture = material.mainTexture as Texture2D;

            return await SendTextureToResoLink(albedoTexture);
        }

        private Mesh SetUV1ToUV0(Mesh mesh) {
            if (!IsConnected()) return null;
            if (mesh == null) return null;

            Mesh duplicatedMesh = UnityEngine.Object.Instantiate(mesh);
            duplicatedMesh.name = mesh.name + "_UV1ToUV0";

            duplicatedMesh.uv = mesh.uv2;
            duplicatedMesh.uv2 = null;

            return duplicatedMesh;
        }

        private Mesh ApplyLightmapOffsetsToMesh(MeshRenderer renderer) {
            if (!IsConnected()) return null;

            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            Mesh mesh = filter?.sharedMesh;
            if (mesh == null) return null;

            if (renderer.lightmapScaleOffset == new Vector4(1f, 1f, 0f, 0f)) return mesh;

            Vector4 offset = renderer.lightmapScaleOffset;
            Vector2[] uv2 = mesh.uv2;
            if (uv2 == null || uv2.Length != mesh.vertexCount) return null;

            Mesh meshCopy = UnityEngine.Object.Instantiate(mesh);
            meshCopy.name = mesh.name + "_OffsetFixed";

            Vector2[] uv2Offset = new Vector2[uv2.Length];
            for (int j = 0; j < uv2.Length; j++) {
                uv2Offset[j] = new Vector2(
                    uv2[j].x * offset.x + offset.z,
                    uv2[j].y * offset.y + offset.w
                );
            }

            meshCopy.uv2 = uv2Offset;
            filter.sharedMesh = meshCopy;
            renderer.lightmapScaleOffset = new Vector4(1f, 1f, 0f, 0f);

            return meshCopy;
        }

        private async Task<string> AddValueMultiDriverConfig(string assetsSlot, string type, Dictionary<string, Member> multiDriverMembers, Dictionary<string, Member> valueDriverMembers) {
            if (!IsConnected()) return null;
            
            string multiDriverId = await AddMultiDriverToResolinkSlot(assetsSlot, type, multiDriverMembers);

            ComponentData multiDriverComponent = await FetchComponent(multiDriverId);
            multiDriverComponent.Data.Members.TryGetValue("Value", out Member multiDriverValue);

            valueDriverMembers.Add("TargetField", new Reference { TargetID = multiDriverValue.ID });         

            string booleanValueDriver = await AddBooleanValueDriverToResolinkSlot(lumosConfigSlot?.Data?.ID, type, valueDriverMembers);

            await AddDestroyProxyToResolinkSlot(assetsSlot, booleanValueDriver);
            await AddDestroyProxyToResolinkSlot(assetsSlot, multiDriverId);

            return multiDriverId;
        }

        private async Task<string> AddValueMultiDriverConfigWithProxy(string assetsSlot, string type, Dictionary<string, Member> multiDriverMembers) {
            if (!IsConnected()) return null;
            
            string multiDriverId = await AddMultiDriverToResolinkSlot(assetsSlot, type, multiDriverMembers);

            ComponentData multiDriverComponent = await FetchComponent(multiDriverId);
            multiDriverComponent.Data.Members.TryGetValue("Value", out Member multiDriverValue);

            string valueFieldId = await AddComponentToResolinkSlot(lumosConfigSlot?.Data?.ID, new ResoniteLink.Component() {
                ComponentType = $"[FrooxEngine]FrooxEngine.ValueField<{type}>",
                Members = multiDriverMembers
            });

            ComponentData valueFieldComponent = await FetchComponent(valueFieldId);
            valueFieldComponent.Data.Members.TryGetValue("Value", out Member value);

            string valueCopyId = await AddComponentToResolinkSlot(assetsSlot, new ResoniteLink.Component() {
                ComponentType = $"[FrooxEngine]FrooxEngine.ValueCopy<{type}>",
                Members = new Dictionary<string, Member>() {
                    { "Source", new Reference { TargetID = value.ID } },
                    { "Target", new Reference { TargetID = multiDriverValue.ID } }
                }
            });

            await AddDestroyProxyToResolinkSlot(assetsSlot, multiDriverId);
            await AddDestroyProxyToResolinkSlot(assetsSlot, valueFieldId);
            await AddDestroyProxyToResolinkSlot(assetsSlot, valueCopyId);

            return multiDriverId;
        }

        public async Task PrepareAndSendToResolink(Action<string, float> progressCallback) {
            if (!IsConnected()) return;

            try {
                if (progressCallback == null) return;

                MeshRenderer[] meshRenderers = UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
                int total = meshRenderers.Length;

                (string, string, string, string) assetIds = await CreateLightmapAssetsSlot();

                string compressionType = "[Elements.Assets]Elements.Assets.TextureCompression?";
                string compressionMultiDriverId = await AddValueMultiDriverConfig(assetIds.Item1, compressionType, new Dictionary<string, Member> {
                    { "Value", new Field_Nullable_Enum { Value = "BC6H_LZMA" } },
                }, new Dictionary<string, Member> {
                    { "FalseValue", new Field_Nullable_Enum { Value = "BC6H_LZMA" } },
                    { "TrueValue", new Field_Nullable_Enum { Value = "RawRGBAHalf" } },
                });

                string colorMultiDriverId = await AddValueMultiDriverConfigWithProxy(assetIds.Item1, "colorX", new Dictionary<string, Member> {
                    { "Value", new Field_colorX { Value = new colorX() { r = 1, g = 1, b = 1, a = 1, Profile = "Linear" } } },
                });

                textureElementsToSend = new List<Member>();
                colorElementsToSend = new List<Member>();

                for (int i = 0; i < total; i++) {
                    Mesh mesh = ApplyLightmapOffsetsToMesh(meshRenderers[i]);
                    if (mesh == null) continue;

                    Mesh duplicatedMesh = SetUV1ToUV0(mesh);
                    if (duplicatedMesh == null) continue;

                    int lightmapIndex = meshRenderers[i].lightmapIndex;
                    if (lightmapIndex < 0 || lightmapIndex >= LightmapSettings.lightmaps.Length) continue;

                    LightmapData lightmapData = LightmapSettings.lightmaps[lightmapIndex];
                    if (lightmapData == null || lightmapData.lightmapColor == null) continue;

                    Texture2D lightmap = lightmapData.lightmapColor;
                    
                    string slotTargetId = GetResoLinkSlotIdFromName(meshRenderers[i].gameObject.name);
                    await SendMeshRendererToResoLink(assetIds, slotTargetId, duplicatedMesh, lightmap);

                    progressCallback?.Invoke($"Applying lightmap offsets... ({i}/{total})", i / (float)total);
                }

                await UpdateListItems(compressionMultiDriverId, $"[FrooxEngine]FrooxEngine.ValueMultiDriver<{compressionType}>", textureElementsToSend);
                await UpdateListItems(colorMultiDriverId, $"[FrooxEngine]FrooxEngine.ValueMultiDriver<colorX>", colorElementsToSend);

                progressCallback?.Invoke("Lightmap offsets applied.", 1f);
            } catch (Exception e) {
                Debug.LogError($"Error applying lightmap offsets: {e.Message}\n{e.StackTrace}");
            }
            await Task.CompletedTask;
        }

        private async Task UpdateListItems(string listId, string type, List<Member> elements) {
            if (!IsConnected()) return;

            await linkInterface.UpdateComponent(new UpdateComponent() {
                Data = new ResoniteLink.Component() {
                    ID = listId,
                    ComponentType = type,
                    Members = new Dictionary<string, Member> {
                        { "Drives",  new SyncList { Elements = elements } }
                    }
                }
            });
        }

        private async Task<(string, string, string, string)> CreateLightmapAssetsSlot() {
            if (!IsConnected()) return (null, null, null, null);

            if (lumosConfigSlot?.Data?.ID == null) {
                SlotData root = await FetchSlot("Root", true);
                await FetchConfig(root);
            }

            string lumosAssetId = await AddSlotToResolink("LumosAssets", lumosConfigSlot?.Data?.ID);

            if (lumosAssetId == null) return (null, null, null, null);

            string meshSlotId = await AddSlotToResolink("Meshes", lumosAssetId);
            string textureSlotId = await AddSlotToResolink("Textures", lumosAssetId);
            string materialSlotId = await AddSlotToResolink("Materials", lumosAssetId);
            
            return (lumosAssetId, meshSlotId, textureSlotId, materialSlotId);
        }

        private async Task<(string, string)> AddLightmapAssetsToSlots((string, string, string, string) assetIds, string meshUri, string lightmapUri) {
            if (!IsConnected()) return (null, null);

            string lumosAssetId = assetIds.Item1;
            string meshSlotId = assetIds.Item2;
            string textureSlotId = assetIds.Item3;
            string materialSlotId = assetIds.Item4;

            string meshId = await AddMeshToResolinkSlot(meshSlotId, meshUri);
            string textureId = await AddTextureToResolinkSlot(textureSlotId, lightmapUri);
            string materialId = await AddMaterialToResolinkSlot(materialSlotId, textureId);

            ComponentData staticTexture = await FetchComponent(textureId);
            staticTexture.Data.Members.TryGetValue("PreferredFormat", out Member format);
            textureElementsToSend.Add(new Reference { TargetID = format.ID });

            ComponentData material = await FetchComponent(materialId);
            material.Data.Members.TryGetValue("TintColor", out Member color);
            colorElementsToSend.Add(new Reference { TargetID = color.ID });

            return (meshId, materialId);
        }

        private async Task SendMeshRendererToResoLink((string, string, string, string) assetIds, string targetId, Mesh mesh, Texture2D lightmap) {
            if (!IsConnected()) return;

            string meshUri = await SendUnityMeshToResoLink(mesh);
            string lightmapUri = await SendTextureToResoLink(lightmap);

            (string, string) assets = await AddLightmapAssetsToSlots(assetIds, meshUri, lightmapUri);
            string meshRendererId = await AddMeshRendererToResolinkSlot(targetId, assets.Item1, assets.Item2);
            await AddDestroyProxyToResolinkSlot(assetIds.Item1, meshRendererId);
        }
    }
}
