using System;
using System.Collections.Generic;
using System.IO;
using Unity.XR.XREAL;
using Unity.XR.XREAL.Samples;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;

namespace UnityAR
{
    /// <summary>
    /// Saves the live environment mesh to one file per anchor and places saved meshes back into the room.
    /// </summary>
    /// <remarks>
    /// Meshes are stored in the local space of the anchor they belong to, and a loaded mesh is shown as a
    /// child of that anchor, so it follows the anchor as XREAL refines its pose. The live mesh comes from XREAL
    /// meshing, split by classification label by <see cref="MeshClassificationFracking"/>.
    /// </remarks>
    public class RoomMeshStore : MonoBehaviour
    {
        [Serializable]
        struct LabelMaterial
        {
            public NRMeshingVertexSemanticLabel label;
            public Material material;
        }

        const string k_FolderName = "RoomMeshes";
        const string k_FileExtension = ".roommesh";

        [SerializeField]
        ARMeshManager m_MeshManager;

        [SerializeField]
        MeshClassificationFracking m_Classification;

        [SerializeField]
        [Tooltip("Material for each classification label of a loaded mesh. Match the live classified mesh prefabs.")]
        List<LabelMaterial> m_LabelMaterials = new List<LabelMaterial>();

        [SerializeField]
        [Tooltip("Material for labels missing from the list above.")]
        Material m_DefaultMaterial;

        readonly List<GameObject> m_LoadedMeshes = new List<GameObject>();
        readonly Dictionary<MeshFilter, NRMeshingVertexSemanticLabel> m_LoadedLabels = new Dictionary<MeshFilter, NRMeshingVertexSemanticLabel>();
        readonly List<int> m_Triangles = new List<int>();
        readonly List<Vector3> m_Vertices = new List<Vector3>();
        readonly List<Vector3> m_Normals = new List<Vector3>();

        /// <summary>
        /// The folder saved room meshes are written to.
        /// </summary>
        public static string folderPath => Path.Combine(Application.persistentDataPath, k_FolderName);

        /// <summary>
        /// Whether the live mesh is being built and shown. False while a saved mesh is loaded.
        /// </summary>
        public bool isLiveScanning => m_MeshManager.enabled;

        /// <summary>
        /// Number of triangles in the current mesh: the live mesh while scanning, otherwise the loaded mesh.
        /// </summary>
        public int currentTriangleCount
        {
            get
            {
                var count = 0;
                foreach (var pair in GetCurrentMesh())
                {
                    if (pair.Key.sharedMesh.subMeshCount > 0)
                        count += (int)pair.Key.sharedMesh.GetIndexCount(0) / 3;
                }
                return count;
            }
        }

        /// <summary>
        /// Whether a room mesh has been saved for the anchor.
        /// </summary>
        public bool HasSavedMesh(string anchorId) => File.Exists(GetPath(anchorId));

        /// <summary>
        /// When the room mesh for the anchor was last saved.
        /// </summary>
        public DateTime GetSaveTime(string anchorId) => File.GetLastWriteTimeUtc(GetPath(anchorId));

        /// <summary>
        /// Deletes the room mesh saved for the anchor, if any.
        /// </summary>
        public void Delete(string anchorId) => File.Delete(GetPath(anchorId));

        /// <summary>
        /// Saves the current mesh and the world origin for the anchor, replacing anything saved for it before.
        /// </summary>
        /// <remarks>
        /// The current mesh is the live mesh while scanning, otherwise the loaded mesh.
        /// </remarks>
        /// <param name="anchorId">The persistent ID of the anchor the mesh belongs to.</param>
        /// <param name="anchor">The anchor's transform.</param>
        /// <returns>The saved mesh.</returns>
        public RoomMeshData Save(string anchorId, Transform anchor)
        {
            var data = new RoomMeshData
            {
                anchorId = anchorId,
                savedAtUtc = DateTime.UtcNow,
                // The world origin is the identity pose in world space.
                originPose = new Pose(anchor.InverseTransformPoint(Vector3.zero), Quaternion.Inverse(anchor.rotation)),
            };

            var worldToAnchor = anchor.worldToLocalMatrix;
            foreach (var pair in GetCurrentMesh())
            {
                var filter = pair.Key;
                var piece = ExtractPiece(filter.sharedMesh, worldToAnchor * filter.transform.localToWorldMatrix);
                if (piece == null)
                    continue;

                piece.label = (byte)pair.Value;
                data.pieces.Add(piece);
            }

            RoomMeshFile.Write(GetPath(anchorId), data);
            return data;
        }

        /// <summary>
        /// Loads the mesh saved for the anchor and shows it in place of the live mesh, which stops scanning.
        /// </summary>
        /// <param name="anchorId">The persistent ID of the anchor the mesh belongs to.</param>
        /// <param name="anchor">The anchor's transform, which the loaded mesh is attached to.</param>
        /// <returns>The loaded mesh, including the saved world origin relative to the anchor.</returns>
        public RoomMeshData Load(string anchorId, Transform anchor)
        {
            var data = RoomMeshFile.Read(GetPath(anchorId));
            ClearLoadedMeshes();
            m_LoadedMeshes.Add(Spawn(data, anchor));
            SetLiveScanning(false);
            return data;
        }

        /// <summary>
        /// Removes any loaded mesh and resumes building the live mesh.
        /// </summary>
        public void ResumeLiveScanning()
        {
            ClearLoadedMeshes();
            SetLiveScanning(true);
        }

        void OnDestroy() => ClearLoadedMeshes();

        static string GetPath(string anchorId)
        {
            var fileName = anchorId;
            foreach (var invalid in Path.GetInvalidFileNameChars())
                fileName = fileName.Replace(invalid, '_');
            return Path.Combine(folderPath, fileName + k_FileExtension);
        }

        void SetLiveScanning(bool scanning)
        {
            m_MeshManager.enabled = scanning;

            // Hide rather than destroy the live mesh: XREAL keeps its meshing state across a stop and start,
            // and MeshClassificationFracking expects blocks it has seen to still exist when they update.
            foreach (var meshFilter in m_MeshManager.meshes)
            {
                if (meshFilter != null)
                    meshFilter.gameObject.SetActive(scanning);
            }
            foreach (var block in m_Classification.MeshFrackingMap.Values)
            {
                foreach (var filter in block.Values)
                {
                    if (filter != null)
                        filter.gameObject.SetActive(scanning);
                }
            }
        }

        // The mesh filters of the current mesh with their classification labels.
        IEnumerable<KeyValuePair<MeshFilter, NRMeshingVertexSemanticLabel>> GetCurrentMesh()
        {
            if (isLiveScanning)
            {
                foreach (var block in m_Classification.MeshFrackingMap.Values)
                {
                    foreach (var pair in block)
                    {
                        if (pair.Value != null && pair.Value.sharedMesh != null)
                            yield return new KeyValuePair<MeshFilter, NRMeshingVertexSemanticLabel>(pair.Value, pair.Key);
                    }
                }
            }
            else
            {
                foreach (var pair in m_LoadedLabels)
                {
                    if (pair.Key != null && pair.Key.sharedMesh != null)
                        yield return pair;
                }
            }
        }

        RoomMeshPiece ExtractPiece(Mesh mesh, Matrix4x4 meshToAnchor)
        {
            if (mesh.subMeshCount == 0)
                return null;

            mesh.GetTriangles(m_Triangles, 0);
            if (m_Triangles.Count == 0)
                return null;

            mesh.GetVertices(m_Vertices);
            mesh.GetNormals(m_Normals);
            var hasNormals = m_Normals.Count == m_Vertices.Count;

            // Each classified mesh shares its block's full vertex list; keep only the vertices its triangles use.
            var remap = new int[m_Vertices.Count];
            for (var i = 0; i < remap.Length; i++)
                remap[i] = -1;

            var vertices = new List<Vector3>();
            var normals = hasNormals ? new List<Vector3>() : null;
            var triangles = new int[m_Triangles.Count];
            for (var i = 0; i < m_Triangles.Count; i++)
            {
                var source = m_Triangles[i];
                if (remap[source] < 0)
                {
                    remap[source] = vertices.Count;
                    vertices.Add(meshToAnchor.MultiplyPoint3x4(m_Vertices[source]));
                    if (hasNormals)
                        normals.Add(meshToAnchor.MultiplyVector(m_Normals[source]).normalized);
                }
                triangles[i] = remap[source];
            }

            return new RoomMeshPiece
            {
                vertices = vertices.ToArray(),
                normals = normals?.ToArray(),
                triangles = triangles,
            };
        }

        GameObject Spawn(RoomMeshData data, Transform anchor)
        {
            // Anchors live under the trackables parent, so the mesh stays fixed in the room when the world origin
            // moves, and BeamRaycast treats it as a detected surface.
            var root = new GameObject("Saved Room Mesh");
            root.transform.SetParent(anchor, false);

            foreach (var piece in data.pieces)
            {
                var label = (NRMeshingVertexSemanticLabel)piece.label;
                var mesh = new Mesh { name = label.ToString() };
                if (piece.vertices.Length > ushort.MaxValue)
                    mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(piece.vertices);
                mesh.SetTriangles(piece.triangles, 0);
                if (piece.normals != null)
                    mesh.SetNormals(piece.normals);
                else
                    mesh.RecalculateNormals();

                var pieceObject = new GameObject(mesh.name);
                pieceObject.transform.SetParent(root.transform, false);
                var filter = pieceObject.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                pieceObject.AddComponent<MeshRenderer>().sharedMaterial = GetMaterial(label);
                pieceObject.AddComponent<MeshCollider>().sharedMesh = mesh;
                m_LoadedLabels[filter] = label;
            }
            return root;
        }

        Material GetMaterial(NRMeshingVertexSemanticLabel label)
        {
            foreach (var labelMaterial in m_LabelMaterials)
            {
                if (labelMaterial.label == label)
                    return labelMaterial.material;
            }
            return m_DefaultMaterial;
        }

        void ClearLoadedMeshes()
        {
            foreach (var root in m_LoadedMeshes)
            {
                if (root == null)
                    continue;
                foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
                    Destroy(filter.sharedMesh);
                Destroy(root);
            }
            m_LoadedMeshes.Clear();
            m_LoadedLabels.Clear();
        }
    }
}
