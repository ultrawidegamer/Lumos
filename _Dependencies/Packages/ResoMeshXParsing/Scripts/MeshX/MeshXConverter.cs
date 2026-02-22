using UnityEngine;

namespace ResoMeshXParsing {
    public static class MeshXConverter {
        public static Mesh ConvertToUnityMesh(MeshXData meshXData) {
            if (meshXData == null || meshXData.Positions.Count == 0) {
                Debug.LogError("Invalid MeshXData");
                return null;
            }

            Mesh mesh = new Mesh();
            mesh.name = "MeshX_Import";

            if (meshXData.VertexCount > 65535) {
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }

            mesh.SetVertices(meshXData.Positions);

            if (meshXData.HasNormals && meshXData.Normals.Count > 0) {
                mesh.SetNormals(meshXData.Normals);
            }

            if (meshXData.HasTangents && meshXData.Tangents.Count > 0) {
                mesh.SetTangents(meshXData.Tangents);
            }

            if (meshXData.HasColors && meshXData.Colors.Count > 0) {
                mesh.SetColors(meshXData.Colors);
            }

            SetUVFirstChannel(mesh, meshXData);

            mesh.subMeshCount = meshXData.Submeshes.Count;
            for (int i = 0; i < meshXData.Submeshes.Count; i++) {
                MeshTopology type = ConvertTopology(meshXData.SubmeshTypes[i]);
                mesh.SetIndices(meshXData.Submeshes[i].ToArray(), type, i);
            }

            mesh.RecalculateBounds();

            if (!meshXData.HasNormals) {
                mesh.RecalculateNormals();
            }

            if (!meshXData.HasTangents && meshXData.UV2DChannels.Count > 0) {
                mesh.RecalculateTangents();
            }

            return mesh;
        }

        private static void SetUVFirstChannel(Mesh mesh, MeshXData meshXData) {
            int channel2D = 0;
            int channel3D = 0;
            int channel4D = 0;

            int dimension = meshXData.UVDimensions[0];

            if (dimension == 2 && channel2D < meshXData.UV2DChannels.Count) {
                mesh.SetUVs(0, meshXData.UV2DChannels[channel2D]);
                channel2D++;
            } else if (dimension == 3 && channel3D < meshXData.UV3DChannels.Count) {
                mesh.SetUVs(0, meshXData.UV3DChannels[channel3D]);
                channel3D++;
            } else if (dimension == 4 && channel4D < meshXData.UV4DChannels.Count) {
                mesh.SetUVs(0, meshXData.UV4DChannels[channel4D]);
                channel4D++;
            }
        }

        private static MeshTopology ConvertTopology(SubmeshType type) {
            switch (type) {
                case SubmeshType.Quads:
                    return MeshTopology.Quads;
                case SubmeshType.Lines:
                    return MeshTopology.Lines;
                case SubmeshType.LineStrip:
                    return MeshTopology.LineStrip;
                case SubmeshType.Points:
                    return MeshTopology.Points;
                default:
                    return MeshTopology.Triangles;
            }
        }

        public static MeshRenderer ApplyMeshToGameObject(Mesh mesh, GameObject targetObject, bool enableRenderer) {
            MeshFilter meshFilter = targetObject.GetComponent<MeshFilter>();
            if (meshFilter == null) { 
                meshFilter = targetObject.AddComponent<MeshFilter>();
            }
            meshFilter.sharedMesh = mesh;

            MeshRenderer meshRenderer = targetObject.GetComponent<MeshRenderer>();
            if (meshRenderer == null) {
                meshRenderer = targetObject.AddComponent<MeshRenderer>();
                meshRenderer.enabled = enableRenderer;
            }

            int subMeshCount = mesh != null ? mesh.subMeshCount : 1;
            Material[] sharedMaterials = meshRenderer.sharedMaterials;
            if (sharedMaterials == null || sharedMaterials.Length != subMeshCount) {
                sharedMaterials = new Material[subMeshCount];
            }

            Shader shader = Shader.Find("Standard");
            for (int i = 0; i < subMeshCount; i++) {
                if (sharedMaterials[i] != null) continue;
                sharedMaterials[i] = new Material(shader);
            }
            meshRenderer.sharedMaterials = sharedMaterials;

            return meshRenderer;
        }
    }
}