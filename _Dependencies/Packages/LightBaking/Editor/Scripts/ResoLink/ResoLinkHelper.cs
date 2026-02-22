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
        private SynchronizationContext unityContext = SynchronizationContext.Current;
        private ConcurrentDictionary<string, SlotData> slotDataLookup = new ConcurrentDictionary<string, SlotData>();
        private ConcurrentDictionary<string, GameObject> createdObjects = new ConcurrentDictionary<string, GameObject>();
        private ConcurrentDictionary<string, ComponentData> cachedMeshComponentData = new ConcurrentDictionary<string, ComponentData>();
        private CancellationTokenSource cancellation = new CancellationTokenSource();

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

        private ResoniteLink.Component GetMeshRenderer(SlotData slotData) {
            return slotData.Data.Components.FirstOrDefault(c => c.ComponentType == "[FrooxEngine]FrooxEngine.MeshRenderer");
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

                    SlotData slotData = await FetchSlot(slot.ID, true);

                    if (slotData == null) return;
                    if (slotData.Data == null) return;

                    List<ResoniteLink.Component> components = slotData.Data.Components;

                    bool hasBlacklistedComponent = components?.Any(c => BlacklistedComponents.Contains(c.ComponentType)) ?? false;

                    if (hasBlacklistedComponent) return;

                    string slotName = slotData.Data?.Name?.Value ?? "Unknown";
                    string slotId = slotData.Data.ID ?? "";
                    string pathSegment = $"{slotName} ({slotId})";
                    List<string> currentPath = new List<string>(parentPath) { pathSegment };

                    ResoniteLink.Component meshRenderer = GetMeshRenderer(slotData);

                    Member meshMember = null;
                    meshRenderer?.Members?.TryGetValue("Mesh", out meshMember);
                    string meshID = (meshMember as Reference)?.TargetID;

                    allSlots[slotData.Data.ID] = new SlotInfo {
                        Data = slotData,
                        Path = currentPath.ToArray(),
                        MeshId = meshID
                    };

                    if (meshRenderer != null && !string.IsNullOrEmpty(meshID)) {
                        slotsWithMeshRenderer[slotData.Data.ID] = allSlots[slotData.Data.ID];

                        unityContext?.Post(_ => {
                            CreateUnityHierarchyPart(allSlots[slotData.Data.ID]);
                        }, null);
                    }

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

        public async Task FetchMeshSlots(Action<string, float> progressCallback = null) {
            if (!IsConnected()) return;

            try {
                SlotData rootSlotData = await FetchSlot("Root", true);

                if (rootSlotData == null) return;
                if (rootSlotData.Data == null) return;


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
                        obj.SetActive(slotData.Data.IsActive.Value);

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

                        ComponentData component = await FetchComponent(slotInfo.MeshId);
                        if (component == null || component.Data == null) return;

                        Member urlMember = null;
                        component.Data.Members.TryGetValue("URL", out urlMember);
                        string meshUri = (urlMember as Field_Uri)?.Value?.ToString();

                        ResoniteLink.Component meshRenderer = GetMeshRenderer(slotInfo.Data);

                        meshRenderer.Members.TryGetValue("Enabled", out Member enabled);

                        if (urlMember == null || meshUri == null) return;

                        Mesh mesh = await AcquireMesh(meshUri);
                        if (mesh == null) return;

                        MeshRenderer renderer = MeshXConverter.ApplyMeshToGameObject(mesh, targetObject, (enabled as Field_bool).Value);
                        
                        GameObjectUtility.SetStaticEditorFlags(
                            targetObject,
                            GameObjectUtility.GetStaticEditorFlags(targetObject) 
                            | StaticEditorFlags.ContributeGI
                        );
                
                        renderer.receiveGI = ReceiveGI.Lightmaps;
                        
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
                
                Unwrapping.GenerateSecondaryUVSet(mesh);

                MeshXCache.Instance.AddToMeshDataCache(meshId, mesh);
                return mesh;
            } catch (Exception e) {
                Debug.LogError($"Error downloading and converting MeshX {meshId}: {e.Message}");
                return null;
            }
        }

        public async Task<bool> SendUnityMeshToResoLink(Mesh mesh) {
            if (!IsConnected() || mesh == null) return false;

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
                    return false;
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

                if (linkInterface == null) return false;
                if (importData == null) return false;

                AssetData data = await linkInterface.ImportMesh(importData);

                Debug.Log($"Mesh import result: {data?.AssetURL}");

                return !string.IsNullOrEmpty(data?.AssetURL?.ToString());
            } catch (Exception e) {
                Debug.LogError($"Exception in SendUnityMeshToResoLink: {e.Message}\n{e.StackTrace}");
                return false;
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

        private async Task<bool> SendHDRTextureToResoLink(Texture2D texture) {
            if (!IsConnected()) return false;

            ImportTexture2DRawDataHDR import = new ImportTexture2DRawDataHDR {
                Width = texture.width,
                Height = texture.height
            };

            FillImportHDRTexture2DRawData(import, texture);
            AssetData assetData = await linkInterface.ImportTexture(import);

            Debug.Log($"HDR Texture import result: {assetData?.AssetURL}");

            return !string.IsNullOrEmpty(assetData?.AssetURL?.ToString());
        }

        private async Task<bool> SendSDRTextureToResoLink(Texture2D texture) {
            if (!IsConnected()) return false;

            ImportTexture2DRawData import = new ImportTexture2DRawData {
                Width = texture.width,
                Height = texture.height
            };

            FillImportSDRTexture2DRawData(import, texture);
            AssetData assetData = await linkInterface.ImportTexture(import);

            Debug.Log($"SDR Texture import result: {assetData?.AssetURL}");

            return !string.IsNullOrEmpty(assetData?.AssetURL?.ToString());
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

        public async Task<bool> SendTextureToResoLink(Texture2D texture) {
            if (!IsConnected()) return false;
            if (texture == null) return false;
            SetTextureReadable(texture);

            if (GraphicsFormatUtility.IsHDRFormat(texture.graphicsFormat)) {
                return await SendHDRTextureToResoLink(texture);
            } else {
                return await SendSDRTextureToResoLink(texture);
            }
        }

        public async Task<bool> SendTextureToResoLinkViaObject(GameObject textureObject) {
            if (!IsConnected()) return false;
            if (textureObject == null) return false;
            Renderer renderer = textureObject.GetComponent<Renderer>();

            if (renderer == null) return false;
            if (renderer.sharedMaterials.Length == 0) return false;
            
            Material material = renderer.sharedMaterials[0];
            Texture2D albedoTexture = material.mainTexture as Texture2D;

            return await SendTextureToResoLink(albedoTexture);
        }
    }
}
