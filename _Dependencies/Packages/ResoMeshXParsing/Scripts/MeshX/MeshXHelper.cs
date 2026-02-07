using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using LZ4;
using UnityEngine;

namespace ResoMeshXParsing {
    public class MeshXHelper {
        private static string baseUrl = "https://assets.resonite.com/";
        private static readonly object lockObj = new object();
        private static readonly int maxSupportedMeshXVersion = 7;
        private static MeshXHelper instance;
        private static BinaryReader binaryReader = null;
        private static HttpClient httpClient = new HttpClient();

        public static MeshXHelper Instance {
            get {
                if (instance == null) {
                    lock (lockObj) {
                        if (instance == null) {
                            instance = new MeshXHelper();
                        }
                    }
                }
                return instance;
            }
        }

        public async Task<MeshXData> DownloadMeshX(string id) {
            httpClient.DefaultRequestHeaders.Add("User-Agent", "LightBaking");
            HttpResponseMessage response = await httpClient.GetAsync(baseUrl + id);
            
            if (response.IsSuccessStatusCode) {
                byte[] bytes = await response.Content.ReadAsByteArrayAsync();

                using (MemoryStream stream = new MemoryStream(bytes)) {
                    return DecodeMeshX(stream);
                }

            } else {
                Debug.LogError($"Failed to download MeshX: {response.StatusCode}");
                return null;
            }
        }

        private MeshXData DecodeMeshX(Stream stream) {
            binaryReader = new BinaryReader(stream);
            int version = CheckMeshXHeaders(stream);
            uint featureFlags = binaryReader.ReadUInt32();

            MeshXData meshXData = new MeshXData {
                FeatureFlags = featureFlags,
                VertexCount = Read7Bits()
            };

            if (meshXData.VertexCount == 0) return null;

            meshXData.Version = version;
            meshXData.HasNormals = (meshXData.FeatureFlags & (1 << 0)) != 0;
            meshXData.HasTangents = (meshXData.FeatureFlags & (1 << 1)) != 0;
            meshXData.HasColors = (meshXData.FeatureFlags & (1 << 2)) != 0;
            meshXData.HasBones = (meshXData.FeatureFlags & (1 << 3)) != 0;

            switch (version) {
                case 1:
                    return FinishDecodingMeshX(meshXData, new Action<MeshXData>[] { 
                        TriangleCountOld,
                        MeshAndBonesOld,
                        UvDimensionsOld,
                        ReadMeshData,
                        ReadBoneBindingsAndNormalize,
                        ReadUVChannels,
                        ReadSubmeshesOld,
                        ReadBones,
                        ReadBlendShapes
                    });
                case 2:
                    return FinishDecodingMeshX(meshXData, new Action<MeshXData>[] { 
                        TriangleCountOld,
                        MeshAndBonesOld,
                        UvDimensionsOld,
                        ReadEncodingName,
                        ChangeEncodingStream,
                        ReadMeshData,
                        ReadBoneBindingsAndNormalize,
                        ReadUVChannels,
                        ReadSubmeshesOld,
                        ReadBones,
                        ReadBlendShapes,
                    });
                case 3:
                case 4:
                    return FinishDecodingMeshX(meshXData, new Action<MeshXData>[] { 
                        MeshAndBonesNew,
                        BlendShapeCount,
                        UvDimensionsOld,
                        ReadEncodingName,
                        ChangeEncodingStream,
                        ReadMeshData,
                        ReadBoneBindingsAndNormalize,
                        ReadUVChannels,
                        ReadSubmeshesNew,
                        ReadBones,
                        ReadBlendShapes
                    });
                case 5:
                    return FinishDecodingMeshX(meshXData, new Action<MeshXData>[] { 
                        MeshAndBonesNew,
                        BlendShapeCount,
                        UvDimensionsOld,
                        ReadEncodingName,
                        ChangeEncodingStream,
                        ReadMeshData,
                        ReadBoneBindings,
                        ReadUVChannels,
                        ReadSubmeshesNew,
                        ReadBones,
                        ReadBlendShapes
                    });
                case 6:
                    return FinishDecodingMeshX(meshXData, new Action<MeshXData>[] { 
                        MeshAndBonesNew,
                        BlendShapeCount,
                        UvDimensionsNew,
                        ReadEncodingName,
                        ChangeEncodingStream,
                        ReadMeshData,
                        ReadBoneBindings,
                        ReadUVChannels,
                        ReadSubmeshesNew,
                        ReadBones,
                        ReadBlendShapes,
                    });
                case 7:
                    return FinishDecodingMeshX(meshXData, new Action<MeshXData>[] { 
                        MeshAndBonesNew,
                        BlendShapeCount,
                        UvDimensionsNew,
                        ReadColorProfile,
                        ReadEncodingName,
                        ChangeEncodingStream,
                        ReadMeshData,
                        ReadBoneBindings,
                        ReadUVChannels,
                        ReadSubmeshesNew,
                        ReadBones,
                        ReadBlendShapes,
                    });
                default:
                    Debug.LogError($"Unsupported MeshX version: {version}");
                    return null;
            }
        }

        private MeshXData FinishDecodingMeshX(MeshXData meshXData, Action<MeshXData>[] decoders) {
            foreach (Action<MeshXData> decoder in decoders) {
                decoder.Invoke(meshXData);
            }

            return meshXData;
        }

        private void ChangeEncodingStream(MeshXData meshXData) {
            BinaryReader currentReader = null;

            switch (meshXData.EncodingName) {
                case "LZ4":
                    Debug.Log("Switching to LZ4 decompression");
                    LZ4Stream lz4Stream = new LZ4Stream(binaryReader.BaseStream, LZ4StreamMode.Decompress);
                    currentReader = new BinaryReader(lz4Stream);
                    break;
                case "LZMA":
                    Debug.LogError("LZMA decompression not yet implemented");
                    currentReader = null;
                    break;
                default:
                    Debug.Log("Using Plain encoding (no compression)");
                    currentReader = binaryReader;
                    break;
            }

            binaryReader = currentReader;
        }

        private void ReadMeshData(MeshXData meshXData) {           
            for (ulong i = 0; i < meshXData.VertexCount; i++) {
                meshXData.Positions.Add(Read3D());
            }

            if (meshXData.HasNormals) {
                for (ulong i = 0; i < meshXData.VertexCount; i++) {
                    meshXData.Normals.Add(Read3D());
                }
            }

            if (meshXData.HasTangents) {
                for (ulong i = 0; i < meshXData.VertexCount; i++) {
                    meshXData.Tangents.Add(Read4D());
                }
            }

            if (meshXData.HasColors) {
                for (ulong i = 0; i < meshXData.VertexCount; i++) {
                    meshXData.Colors.Add(ReadColor());
                }
            }
        }

        private void ReadUVChannels(MeshXData meshXData) {
            for (int x = 0; x < meshXData.UVCount; x++) {
                switch (meshXData.UVDimensions[x]) {
                    case 2:
                        List<Vector2> uv2DList = new List<Vector2>();
                        for (ulong i = 0; i < meshXData.VertexCount; i++) {
                            uv2DList.Add(Read2D());
                        }
                        meshXData.UV2DChannels.Add(uv2DList);
                        break;
                    
                    case 3:
                        List<Vector3> uv3DList = new List<Vector3>();
                        for (ulong i = 0; i < meshXData.VertexCount; i++) {
                            uv3DList.Add(Read3D());
                        }
                        meshXData.UV3DChannels.Add(uv3DList);
                        break;
                    
                    case 4:
                        List<Vector4> uv4DList = new List<Vector4>();
                        for (ulong i = 0; i < meshXData.VertexCount; i++) {
                            uv4DList.Add(Read4D());
                        }
                        meshXData.UV4DChannels.Add(uv4DList);
                        break;
                }
            }
        }

        private void ReadSubmeshesOld(MeshXData meshXData) {
            if (meshXData.TriangleSubmeshCount == 1) {
                List<int> indices = new List<int>();
                for (int i = 0; i < meshXData.TriangleCount; i++) {
                    indices.Add(binaryReader.ReadInt32());
                    indices.Add(binaryReader.ReadInt32());
                    indices.Add(binaryReader.ReadInt32());
                }
                meshXData.Submeshes.Add(indices);
                meshXData.SubmeshTypes.Add(SubmeshType.Triangles);
            } else {
                int[] triangles = new int[meshXData.TriangleCount * 3];
                int[] submeshIndices = new int[meshXData.TriangleCount];
                    
                for (int i = 0; i < meshXData.TriangleCount; i++) {
                    triangles[i * 3] = binaryReader.ReadInt32();
                    triangles[i * 3 + 1] = binaryReader.ReadInt32();
                    triangles[i * 3 + 2] = binaryReader.ReadInt32();
                }
                    
                for (int i = 0; i < meshXData.TriangleCount; i++) {
                    submeshIndices[i] = binaryReader.ReadInt32();
                }
                    
                for (int submeshIndex = 0; submeshIndex < meshXData.TriangleSubmeshCount; submeshIndex++) {
                    List<int> indices = new List<int>();
                        
                    for (int i = 0; i < meshXData.TriangleCount; i++) {
                        if (submeshIndices[i] == submeshIndex) {
                            indices.Add(triangles[i * 3]);
                            indices.Add(triangles[i * 3 + 1]);
                            indices.Add(triangles[i * 3 + 2]);
                        }
                    }
                        
                    meshXData.Submeshes.Add(indices);
                    meshXData.SubmeshTypes.Add(SubmeshType.Triangles);
                }
            }
        }

        private void ReadSubmeshesNew(MeshXData meshXData) {
            for (int i = 0; i < meshXData.SubmeshCount; i++) {
                string name = binaryReader.ReadString();
                    
                if (string.IsNullOrEmpty(name)) continue;
                
                SubmeshType type;

                try {
                    type = (SubmeshType)Enum.Parse(typeof(SubmeshType), name, true);
                } catch (ArgumentException ex) {
                    Debug.LogError($"Failed to parse submesh name: {ex.Message}");
                    continue;
                }
                    
                meshXData.SubmeshTypes.Add(type);
                    
                List<int> indices = new List<int>();
                int count = type == SubmeshType.Triangles ? (int)Read7Bits() * 3 : (int)Read7Bits();
  
                for (int j = 0; j < count; j++) {
                    indices.Add(binaryReader.ReadInt32());
                }
                    
                meshXData.Submeshes.Add(indices);
            }
        }

        private void ReadBoneBindings(MeshXData meshXData) {
            if (!meshXData.HasBones) return;

            for (ulong i = 0; i < meshXData.VertexCount; i++) {
                BoneBinding binding = ReadBoneBinding();                 
                meshXData.BoneBindings.Add(binding);
            }
        }

        private void ReadBoneBindingsAndNormalize(MeshXData meshXData) {
            if (!meshXData.HasBones) return;

            for (ulong i = 0; i < meshXData.VertexCount; i++) {
                BoneBinding binding = ReadBoneBinding();
                binding.Normalize();        
                meshXData.BoneBindings.Add(binding);
            }
        }

        private void ReadBones(MeshXData meshXData) {
            for (int i = 0; i < meshXData.BoneCount; i++) {
                meshXData.Bones.Add(new BoneData {
                    name = binaryReader.ReadString(),
                    bindPose = Read4x4()
                });
            }
        }

        private void ReadBlendShapes(MeshXData meshXData) {
            for (int i = 0; i < meshXData.BlendShapeCount; i++) {
                string name = binaryReader.ReadString();

                uint flags = (uint)Read7Bits();
                bool hasNormals = (flags & (1 << 0)) != 0;
                bool hasTangents = (flags & (1 << 1)) != 0;
                int frameCount = (int)Read7Bits();
                
                if (string.IsNullOrEmpty(name) || frameCount <= 0) return;

                BlendShapeData blendShape = new BlendShapeData {
                    name = name,
                    frames = new List<BlendShapeFrame>()
                };
                    
                for (int frameIndex = 0; frameIndex < frameCount; frameIndex++) {
                    BlendShapeFrame frame = new BlendShapeFrame {
                        weight = binaryReader.ReadSingle(),
                        deltaPositions = new List<Vector3>(),
                        deltaNormals = new List<Vector3>(),
                        deltaTangents = new List<Vector3>()
                    };
                                                
                    for (ulong v = 0; v < meshXData.VertexCount; v++) {
                        frame.deltaPositions.Add(Read3D());
                    }
                        
                    if (hasNormals) {
                        for (ulong v = 0; v < meshXData.VertexCount; v++) {
                            frame.deltaNormals.Add(Read3D());
                        }
                    }

                    if (hasTangents) {
                        for (ulong v = 0; v < meshXData.VertexCount; v++) {
                            frame.deltaTangents.Add(Read3D());
                        }
                    }
                        
                    blendShape.frames.Add(frame);
                }
                    
                meshXData.BlendShapes.Add(blendShape);
                
            }
        }

        private int CheckMeshXHeaders(Stream stream) {
            long pos = stream.Position;
            
            if (!IsMeshXHeader()) {
                Debug.Log("Not a MeshX file, trying LZ4 decompression");

                stream.Position = pos;

                LZ4Stream lz4Stream = new LZ4Stream(stream, LZ4StreamMode.Decompress);
                binaryReader = new BinaryReader(lz4Stream);

                if (!IsMeshXHeader()) {
                    Debug.LogError("Invalid MeshX file");
                    return 0;
                }
            }

            int version = binaryReader.ReadInt32();

            Debug.Log($"MeshX version: {version}");

            if (version > maxSupportedMeshXVersion) {
                Debug.LogError("MeshX version is too new");
                return 0;
            }

            return version;
        }

        private void TriangleCountOld(MeshXData meshXData) {
            meshXData.TriangleCount = (int)Read7Bits();
        }

        private void MeshAndBonesOld(MeshXData meshXData) {
            meshXData.BoneCount = (int)Read7Bits();
            meshXData.TriangleSubmeshCount = (int)Read7Bits();
        }

        private void MeshAndBonesNew(MeshXData meshXData) {
            meshXData.SubmeshCount = (int)Read7Bits();
            meshXData.BoneCount = (int)Read7Bits();
        }

        private void UvDimensionsOld(MeshXData meshXData) {
            uint uvFlags = meshXData.FeatureFlags >> 4;

            for (int i = 0; i < 4; i++) {
                meshXData.UVDimensions[i] = ((uvFlags & (1 << i)) != 0) ? 2 : 0;

                if (meshXData.UVDimensions[i] > 0) {
                    meshXData.UVCount++;
                }
            }
        }

        private void UvDimensionsNew(MeshXData meshXData) {
            meshXData.UVCount = (int)Read7Bits();
            
            for (int i = 0; i < meshXData.UVCount; i++) {
                meshXData.UVDimensions[i] = binaryReader.ReadByte();
            }
        }

        private void BlendShapeCount(MeshXData meshXData) {
            meshXData.BlendShapeCount = (int)Read7Bits();
        }
        private bool IsMeshXHeader() {
            byte[] meshxHeader = new byte[6] { (byte)5, (byte)'M', (byte)'e', (byte)'s', (byte)'h', (byte)'X' };
            byte first = binaryReader.ReadByte();
            byte m = binaryReader.ReadByte();
            byte e = binaryReader.ReadByte();
            byte s = binaryReader.ReadByte();
            byte h = binaryReader.ReadByte();
            byte x = binaryReader.ReadByte();
            byte[] header = new byte[6] { first, m, e, s, h, x };

            return meshxHeader.SequenceEqual(header);
        }

        private void ReadColorProfile(MeshXData meshXData) {
            meshXData.ColorProfile = binaryReader.ReadString();
        }

        private void ReadEncodingName(MeshXData meshXData) {   
            switch (binaryReader.ReadByte()) {
                case 1:
                    meshXData.EncodingName = "LZ4";
                    break;
                case 2:
                    meshXData.EncodingName = "LZMA";
                    break;
                default:
                    meshXData.EncodingName = "Plain";
                    break;
            }
        }

        private BoneBinding ReadBoneBinding() {
            return new BoneBinding {
                boneIndex0 = (int)Read7Bits(),
                boneIndex1 = (int)Read7Bits(),
                boneIndex2 = (int)Read7Bits(),
                boneIndex3 = (int)Read7Bits(),
                weight0 = binaryReader.ReadSingle(),
                weight1 = binaryReader.ReadSingle(),
                weight2 = binaryReader.ReadSingle(),
                weight3 = binaryReader.ReadSingle()
            };
        }

        private ulong Read7Bits() {
            ulong result = 0;
            int shift = 0;

            while (true) {
                byte byteRead = binaryReader.ReadByte();
                byte byteData = (byte)(byteRead & 0x7F);

                result |= (ulong)byteData << shift;
                
                if ((byteRead & 0x80) == 0) break;

                shift += 7;
            }

            return result;
        }

        private Vector2 Read2D() {
            return new Vector2(
                binaryReader.ReadSingle(), 
                binaryReader.ReadSingle()
            );
        }

        private Vector3 Read3D() {
            return new Vector3(
                binaryReader.ReadSingle(), 
                binaryReader.ReadSingle(), 
                binaryReader.ReadSingle()
            );
        }

        private Vector4 Read4D() {
            return new Vector4(
                binaryReader.ReadSingle(), 
                binaryReader.ReadSingle(), 
                binaryReader.ReadSingle(), 
                binaryReader.ReadSingle()
            );
        }

        private Color ReadColor() {
            return new Color(
                binaryReader.ReadSingle(), 
                binaryReader.ReadSingle(), 
                binaryReader.ReadSingle(), 
                binaryReader.ReadSingle()
            );
        }

        private Matrix4x4 Read4x4() {
            Matrix4x4 matrix = new Matrix4x4();
            for (int i = 0; i < 16; i++) {
                matrix[i] = binaryReader.ReadSingle();
            }
            return matrix;
        }
    }
}
