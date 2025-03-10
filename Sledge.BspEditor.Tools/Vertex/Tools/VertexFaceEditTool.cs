﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using System.Windows.Forms;
using LogicAndTrick.Oy;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Modification.Operations.Selection;
using Sledge.BspEditor.Modification.Operations.Tree;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.BspEditor.Rendering.Resources;
using Sledge.BspEditor.Rendering.Viewport;
using Sledge.BspEditor.Tools.Vertex.Controls;
using Sledge.BspEditor.Tools.Vertex.Selection;
using Sledge.Common.Translations;
using Sledge.DataStructures.Geometric;
using Sledge.Rendering.Cameras;
using Sledge.Rendering.Pipelines;
using Sledge.Rendering.Primitives;
using Sledge.Rendering.Resources;
using Sledge.Shell.Input;

namespace Sledge.BspEditor.Tools.Vertex.Tools
{
	[AutoTranslate]
    [Export(typeof(VertexSubtool))]
    public class VertexFaceEditTool : VertexSubtool
    {
        private readonly VertexEditFaceControl _control;

        public override string OrderHint => "F";
        public override string GetName() => "Face editing";
        public override Control Control => _control;
        
        private readonly List<SolidFace> _selectedFaces;

        [ImportingConstructor]
        public VertexFaceEditTool(
            [Import] Lazy<VertexEditFaceControl> control
        )
        {
            _control = control.Value;
            _selectedFaces = new List<SolidFace>();
        }

        protected override IEnumerable<Subscription> Subscribe()
        {
            yield return Oy.Subscribe<string>("VertexTool:DeselectAll", _ => ClearSelection());
            yield return Oy.Subscribe<int>("VertexEditFaceTool:Poke", v => Poke(v));
            yield return Oy.Subscribe<int>("VertexEditFaceTool:Bevel", v => Bevel(v));
            yield return Oy.Subscribe<int>("VertexEditFaceTool:Extrude", v => Extrude(v));
		}
        private void Extrude(int num)
        {
			foreach (var solidFace in _selectedFaces)
			{
				BevelFace(solidFace, num, false);
			}
			UpdateSolids(_selectedFaces.Select(x => x.Solid).ToList());
		}
        private void Poke(int num)
        {
            foreach (var solidFace in _selectedFaces)
            {
                PokeFace(solidFace, num);
            }
            UpdateSolids(_selectedFaces.Select(x => x.Solid).ToList());
        }

        private void Bevel(int num)
        {
            foreach (var solidFace in _selectedFaces)
            {
                BevelFace(solidFace, num);
            }
            UpdateSolids(_selectedFaces.Select(x => x.Solid).ToList());
        }

        private IList<MutableFace> GetSelectedFaces()
        {
            return _selectedFaces.Select(x => x.Face).ToList();
        }

        private void ClearSelection()
        {
            _selectedFaces.Clear();
            Invalidate();
        }

        #region Edit faces

        private void PokeFace(SolidFace solidFace, int num)
        {
            var face = solidFace.Face;
            var solid = solidFace.Solid.Copy;

            // Remove the face
            solid.Faces.Remove(face);

            var center = face.Origin + face.Plane.Normal * num;
            foreach (var edge in face.GetEdges())
            {
                var v1 = face.Vertices.First(x => x.Position.EquivalentTo(edge.Start));
                var v2 = face.Vertices.First(x => x.Position.EquivalentTo(edge.End));
                var verts = new[] { v1.Position, v2.Position, center };
                var f = new MutableFace(verts, face.Texture.Clone());
                solid.Faces.Add(f);
            }
        }

        private void BevelFace(SolidFace solidFace, int num, bool bevel = true)
        {
            var face = solidFace.Face;
            var solid = solidFace.Solid.Copy;

            // Remember the original positions
            var vertexPositions = face.Vertices.Select(x => x.Position).ToList();

            // Scale the face a bit and move it away by the bevel distance
            var origin = face.Origin;
            face.Transform(Matrix4x4.CreateScale(Vector3.One * (bevel?0.9f:1f), origin));
            face.Transform(Matrix4x4.CreateTranslation(face.Plane.Normal * num));

            var vertList = face.Vertices.ToList();

			var document = GetDocument();

			var parentGroup = solidFace.Solid.Real.Hierarchy.Parent as Group;
            if(parentGroup == null)
            {
                parentGroup = new Group(document.Map.NumberGenerator.Next("MapObject"));
                var groupTransaction = new Transaction(new Attach(solidFace.Solid.Real.Hierarchy.Parent.ID, parentGroup));
                groupTransaction.Add(new Attach(parentGroup.ID, solidFace.Solid.Real));
                MapDocumentOperation.Perform(document, groupTransaction);
            }


			var solid1 = new Solid(document.Map.NumberGenerator.Next("MapObject"));
            var solid1v = new VertexSolid(solid1);
            var solid12 = solid1v.Copy;

			var transaction = new Transaction();
			transaction.Add(new Attach(document.Map.Root.ID, solid1v.Real));
			// Create a face for each new edge -> old edge
			foreach (var edge in face.GetEdges())
            {
                var startIndex = vertList.FindIndex(x => x.Position.EquivalentTo(edge.Start));
                var endIndex = vertList.FindIndex(x => x.Position.EquivalentTo(edge.End));
				var verts = new[] { vertexPositions[startIndex], vertexPositions[endIndex], edge.End, edge.Start };
                var texture = face.Texture.Clone();
                {
                    var neighbouringFace = solid.Faces.FirstOrDefault(f =>
                    f.Vertices.Where(v => v.Position == vertexPositions[startIndex]).Any() &&
                    f.Vertices.Where(v => v.Position == vertexPositions[endIndex]).Any()
                    );
                    if(neighbouringFace != null)
                    {
                        texture = neighbouringFace.Texture.Clone();
                        if (neighbouringFace.Texture.Name.ToLower().Equals("null"))
                            texture.Name = face.Texture.Name;
                    }
				}
                var f = new Face(document.Map.NumberGenerator.Next("Face"))
                {
                    Plane = new DataStructures.Geometric.Plane(verts[0], verts[1], verts[3]),
                    Texture = texture
                };
                f.Vertices.AddRange(verts);
                solid1.Data.Add(f);

			}
			if (!bevel)
            {
                solid.Faces.Remove(face);

                var tex = face.Texture.Clone();
                tex.Name = "null";

				var f = new MutableFace(vertexPositions, tex);


                var fb = new MutableFace(vertexPositions.Reverse<Vector3>(), tex);

                solid.Faces.Add(f);
				solid1.Data.Add(fb.ToFace(document.Map.NumberGenerator));
                solid1.Data.Add(face.ToFace(document.Map.NumberGenerator));
                solid1v.IsDirty = true;

                transaction.Add(new Select(solid1));
                transaction.Add(new Attach(parentGroup.ID, solid1));

			}
            solid1.DescendantsChanged();
			MapDocumentOperation.Perform(document, transaction);
		}

		#endregion

		#region 3D interaction

		private Vector3? GetIntersectionPoint(MutableFace face, Line line)
        {
            return new Polygon(face.Vertices.Select(x => x.Position)).GetIntersectionPoint(line);
        }
        
        protected override void MouseDown(MapDocument document, MapViewport viewport, PerspectiveCamera camera,
            ViewportEvent e)
        {
            if (e.Button != MouseButtons.Left) return;

            e.Handled = true;

            // First, get the ray that is cast from the clicked point along the viewport frustrum
            var (start, end) = camera.CastRayFromScreen(new Vector3(e.X, e.Y, 0));
            var ray = new Line(start, end);

            // Grab all the elements that intersect with the ray
            var hits = Selection.Where(x => x.Copy.BoundingBox.IntersectsWith(ray));

            // Sort the list of intersecting elements by distance from ray origin
            var clickedFace = hits
                .SelectMany(x => x.Copy.Faces.Select(f => new { Solid = x, Face = f}))
                .Select(x => new { Item = x, Intersection = GetIntersectionPoint(x.Face, ray) })
                .Where(x => x.Intersection != null)
                .OrderBy(x => (x.Intersection.Value - ray.Start).Length())
                .Select(x => x.Item)
                .FirstOrDefault();
            

            var faces = new List<SolidFace>();
            if (clickedFace != null)
            {
                if (KeyboardState.Shift) faces.AddRange(clickedFace.Solid.Copy.Faces.Select(x => new SolidFace(clickedFace.Solid, x)));
                else faces.Add(new SolidFace(clickedFace.Solid, clickedFace.Face));
            }
            
            if (!KeyboardState.Ctrl) ClearSelection();
            _selectedFaces.AddRange(faces);

            Invalidate();
        }

        #endregion

        public override async Task SelectionChanged()
        {
            ClearSelection();
        }

        public override async Task ToolSelected()
        {
            ClearSelection();
            await base.ToolSelected();
        }

        public override async Task ToolDeselected()
        {
            ClearSelection();
            await base.ToolDeselected();
        }

        public override void Update()
        {
            ClearSelection();
        }

        private void UpdateSolids(List<VertexSolid> solids)
        {
            if (!solids.Any()) return;

            foreach (var solid in solids)
            {
                solid.IsDirty = true;
            }

            Invalidate();
        }

        protected override void Render(MapDocument document, BufferBuilder builder, ResourceCollector resourceCollector)
        {
            base.Render(document, builder, resourceCollector);

            var verts = new List<VertexStandard>();
            var indices = new List<int>();
            var groups = new List<BufferGroup>();

            var col = Vector4.One;
            var tintCol = Color.FromArgb(128, Color.OrangeRed).ToVector4();

            foreach (var face in _selectedFaces)
            {
                var vo = verts.Count;
                var io = indices.Count;

                verts.AddRange(face.Face.Vertices.Select(x => new VertexStandard
                {
                    Position = x.Position,
                    Colour = col,
                    Tint = tintCol,
                    Flags = VertexFlags.FlatColour
                }));

                for (var i = 2; i < face.Face.Vertices.Count; i++)
                {
                    indices.Add(vo);
                    indices.Add(vo + i - 1);
                    indices.Add(vo + i);
                }

                groups.Add(new BufferGroup(PipelineType.TexturedAlpha, CameraType.Perspective, face.Face.Origin, (uint) io, (uint)(indices.Count - io)));
            }

            builder.Append(verts, indices.Select(x => (uint) x), groups);
        }

        private class SolidFace
        {
            public VertexSolid Solid { get; set; }
            public MutableFace Face { get; set; }

            public SolidFace(VertexSolid solid, MutableFace face)
            {
                Solid = solid;
                Face = face;
            }
        }
    }
}