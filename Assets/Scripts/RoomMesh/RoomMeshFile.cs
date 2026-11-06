using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace UnityAR
{
    /// <summary>
    /// The triangles of one classification label from one environment mesh block.
    /// </summary>
    public class RoomMeshPiece
    {
        /// <summary>The <c>NRMeshingVertexSemanticLabel</c> of these triangles.</summary>
        public byte label;

        /// <summary>Vertex positions, relative to the scan image the room was saved to.</summary>
        public Vector3[] vertices;

        /// <summary>Vertex normals in the same space as <see cref="vertices"/>, or null if the mesh had none.</summary>
        public Vector3[] normals;

        /// <summary>Triangle vertex indices.</summary>
        public int[] triangles;
    }

    /// <summary>
    /// A cube placed in the room.
    /// </summary>
    public struct SavedCube
    {
        /// <summary>The cube's center and rotation, relative to the scan image the room was saved to.</summary>
        public Pose pose;

        /// <summary>The length of the cube's edges, in meters.</summary>
        public float size;
    }

    /// <summary>
    /// A room mesh, the cubes placed in the room and the world origin, stored relative to a scan image so all of
    /// them can be placed again wherever that image is recognized in a later session.
    /// </summary>
    public class RoomMeshData
    {
        /// <summary>The key the room was saved under.</summary>
        public string key;

        /// <summary>When the room was saved.</summary>
        public DateTime savedAtUtc;

        /// <summary>The world origin's pose relative to the scan image when the room was saved.</summary>
        public Pose originPose = Pose.identity;

        /// <summary>The mesh pieces.</summary>
        public readonly List<RoomMeshPiece> pieces = new List<RoomMeshPiece>();

        /// <summary>The cubes placed in the room.</summary>
        public readonly List<SavedCube> cubes = new List<SavedCube>();

        /// <summary>Total number of triangles in all pieces.</summary>
        public int triangleCount
        {
            get
            {
                var count = 0;
                foreach (var piece in pieces)
                    count += piece.triangles.Length / 3;
                return count;
            }
        }
    }

    /// <summary>
    /// Reads and writes <see cref="RoomMeshData"/> as a compact binary file.
    /// </summary>
    public static class RoomMeshFile
    {
        const uint k_Magic = 0x4D52_4155; // "UARM"
        const int k_Version = 3;

        // Version 2 files have no cubes.
        const int k_OldestReadableVersion = 2;

        /// <summary>
        /// Writes <paramref name="data"/> to <paramref name="path"/>, replacing any existing file.
        /// </summary>
        public static void Write(string path, RoomMeshData data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            // Write to a temporary file first so an interrupted save never leaves a truncated mesh behind.
            var tempPath = path + ".tmp";
            using (var writer = new BinaryWriter(File.Create(tempPath)))
            {
                writer.Write(k_Magic);
                writer.Write(k_Version);
                writer.Write(data.key ?? string.Empty);
                writer.Write(data.savedAtUtc.ToBinary());
                WritePose(writer, data.originPose);
                writer.Write(data.pieces.Count);
                foreach (var piece in data.pieces)
                {
                    writer.Write(piece.label);
                    WriteVectors(writer, piece.vertices);
                    var hasNormals = piece.normals != null && piece.normals.Length == piece.vertices.Length;
                    writer.Write(hasNormals);
                    if (hasNormals)
                        WriteVectors(writer, piece.normals);
                    writer.Write(piece.triangles.Length);
                    foreach (var index in piece.triangles)
                        writer.Write(index);
                }
                writer.Write(data.cubes.Count);
                foreach (var cube in data.cubes)
                {
                    WritePose(writer, cube.pose);
                    writer.Write(cube.size);
                }
            }

            if (File.Exists(path))
                File.Delete(path);
            File.Move(tempPath, path);
        }

        /// <summary>
        /// Reads a room mesh written by <see cref="Write"/>, including files written by older versions.
        /// </summary>
        /// <exception cref="InvalidDataException">The file is not a room mesh file or has an unsupported version.</exception>
        public static RoomMeshData Read(string path)
        {
            using var reader = new BinaryReader(File.OpenRead(path));
            if (reader.ReadUInt32() != k_Magic)
                throw new InvalidDataException($"{path} is not a room mesh file.");
            var version = reader.ReadInt32();
            if (version < k_OldestReadableVersion || version > k_Version)
                throw new InvalidDataException($"{path} has unsupported room mesh version {version}.");

            var data = new RoomMeshData
            {
                key = reader.ReadString(),
                savedAtUtc = DateTime.FromBinary(reader.ReadInt64()),
                originPose = ReadPose(reader),
            };
            var pieceCount = reader.ReadInt32();
            for (var i = 0; i < pieceCount; i++)
            {
                var piece = new RoomMeshPiece { label = reader.ReadByte(), vertices = ReadVectors(reader) };
                if (reader.ReadBoolean())
                    piece.normals = ReadVectors(reader);
                piece.triangles = new int[reader.ReadInt32()];
                for (var t = 0; t < piece.triangles.Length; t++)
                    piece.triangles[t] = reader.ReadInt32();
                data.pieces.Add(piece);
            }

            if (version >= 3)
            {
                var cubeCount = reader.ReadInt32();
                for (var i = 0; i < cubeCount; i++)
                    data.cubes.Add(new SavedCube { pose = ReadPose(reader), size = reader.ReadSingle() });
            }
            return data;
        }

        static void WritePose(BinaryWriter writer, Pose pose)
        {
            WriteVector(writer, pose.position);
            writer.Write(pose.rotation.x);
            writer.Write(pose.rotation.y);
            writer.Write(pose.rotation.z);
            writer.Write(pose.rotation.w);
        }

        static Pose ReadPose(BinaryReader reader)
        {
            return new Pose(
                ReadVector(reader),
                new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()));
        }

        static void WriteVectors(BinaryWriter writer, Vector3[] vectors)
        {
            writer.Write(vectors.Length);
            foreach (var v in vectors)
                WriteVector(writer, v);
        }

        static Vector3[] ReadVectors(BinaryReader reader)
        {
            var vectors = new Vector3[reader.ReadInt32()];
            for (var i = 0; i < vectors.Length; i++)
                vectors[i] = ReadVector(reader);
            return vectors;
        }

        static void WriteVector(BinaryWriter writer, Vector3 v)
        {
            writer.Write(v.x);
            writer.Write(v.y);
            writer.Write(v.z);
        }

        static Vector3 ReadVector(BinaryReader reader)
        {
            return new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        }
    }
}
