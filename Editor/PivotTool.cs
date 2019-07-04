using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.EditorTools;
using System.Reflection;
using UnityEditor.ShortcutManagement;
using System.Text.RegularExpressions;

namespace uTools
{
    [EditorTool("Pivot Tool", typeof(Transform))]
    public class PivotTool : EditorTool
    {
        [SerializeField]
        private GUIContent m_IconContent;
        private Event e;
        private Transform pivot;
        private IEnumerable<Object> selection;
        private Vector3 pivotPosition;
        private Quaternion pivotRotation;
        private Vector3 pivotScale;
        private Transform rotationReference;
        private bool snapPivot;
        private bool adjustPivot;
        private List<TransformEntry> selectedObjects = new List<TransformEntry>();
        private KeyCode deleteKeycode;
        private SceneView view;
        private Ray ray;
        private RaycastHit hit;
        private object hitTemp;
        private MeshFilter currentMeshFilter;
        private SpriteRenderer currentSpriteRenderer;
        private Edge[] currentEdges;
        private Vector3 adjustedPoint;
        private bool snapped;
        private float snapTolerance = 25;

        private KeyCode adjustKey;
        private KeyCode snapKey;

        private GUIStyle toggleButton;

        private static string packagePath;// = "Packages/com.ltk.pivot/";
        private static PivotTool instance;

        public struct Edge
        {
            public Vector3 v0;
            public Vector3 v1;
            public Vector3 n;

            public Edge(Vector3 _v0, Vector3 _v1, Vector3 _n)
            {
                v0 = _v0;
                v1 = _v1;
                n = _n;
            }
        }

        public class TransformEntry
        {
            public Transform Transform;
            public Transform Pivot;
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Scale;

            public TransformEntry(Transform _transform, ref Transform pivot)
            {
                Transform = _transform;
                Position = _transform.position;
                Rotation = _transform.rotation;
                Scale = _transform.localScale;
                Pivot = new GameObject().transform;
                Pivot.gameObject.hideFlags = HideFlags.HideAndDontSave;
                Pivot.SetPositionAndRotation(Position, Rotation);
                Pivot.localScale = Scale;
                Pivot.SetParent(pivot);
            }
        }

        public override GUIContent toolbarIcon
        {
            get
            {
                if (m_IconContent == null)
                {
                    m_IconContent = new GUIContent(AssetDatabase.LoadAssetAtPath<Texture2D>(packagePath + "Icons/T_PivotIcon.png"), "Pivot Tool");
                }
                return m_IconContent;
            }
        }

        [ClutchShortcut("PivotTool/Adjust Pivot", typeof(SceneView), KeyCode.P)]
        static void AdjustPivot(ShortcutArguments args)
        {
            if (!EditorTools.IsActiveTool(instance))
            {
                return;
            }
            instance.AdjustPivot(args.stage == ShortcutStage.Begin);
        }

        [ClutchShortcut("PivotTool/Snap Pivot", typeof(SceneView), KeyCode.S)]
        static void SnapPivot(ShortcutArguments args)
        {
            if (!EditorTools.IsActiveTool(instance))
            {
                return;
            }
            instance.SnapPivot(args.stage == ShortcutStage.Begin);
        }

        void OnEnable()
        {
            string[] search = AssetDatabase.FindAssets("t:asmdef PivotTool");
            if (search.Length > 0)
            {
                packagePath = Regex.Match(AssetDatabase.GUIDToAssetPath(search[0]), ".*\\/").ToString();
            }
            EditorTools.activeToolChanged += OnActiveToolChange;
        }

        void OnDisable()
        {
            EditorTools.activeToolChanged -= OnActiveToolChange;
        }

        void OnActiveToolChange()
        {
            if (!EditorTools.IsActiveTool(this))
            {
                Undo.undoRedoPerformed -= UndoCallback;
                foreach (var entry in selectedObjects)
                {
                    if (entry.Transform == null)
                    {
                        continue;
                    }
                    entry.Transform.hideFlags = HideFlags.None;
                    if (entry.Pivot)
                    {
                        DestroyImmediate(entry.Pivot.gameObject);
                    }
                }
                if (pivot)
                {
                    DestroyImmediate(pivot.gameObject);
                }
                instance = null;
                return;
            }
            adjustKey = ShortcutManager.instance.GetShortcutBinding("PivotTool/Adjust Pivot").keyCombinationSequence.FirstOrDefault().keyCode;
            snapKey = ShortcutManager.instance.GetShortcutBinding("PivotTool/Snap Pivot").keyCombinationSequence.FirstOrDefault().keyCode;
            instance = this;
            Undo.undoRedoPerformed += UndoCallback;
            pivot = new GameObject("Pivot").transform;
            pivot.gameObject.hideFlags = HideFlags.HideAndDontSave;

            var deleteBinding = ShortcutManager.instance.GetShortcutBinding("Main Menu/Edit/Delete").keyCombinationSequence;
            if (deleteBinding.Count() > 0)
            {
                deleteKeycode = deleteBinding.First().keyCode;
            }
            else
            {
                deleteKeycode = KeyCode.Delete;
            }

            OnSelectionChange();
        }

        void UndoCallback()
        {
            pivotPosition = pivot.position;
            pivotRotation = pivot.rotation;
            pivotScale = pivot.localScale;
            foreach (var entry in selectedObjects)
            {
                entry.Position = entry.Pivot.position;
                entry.Rotation = entry.Pivot.rotation;
            }
            if (currentMeshFilter)
            {
                currentEdges = GetMeshEdges(currentMeshFilter);
            }
            if (currentSpriteRenderer)
            {
                currentEdges = GetMeshEdges(currentSpriteRenderer);
            }
        }

        void OnSelectionChange()
        {
            var oldSelection = selection;
            selection = targets;
            if (oldSelection != null && selection != oldSelection)
            {
                if (oldSelection.Count() > selection.Count())
                {
                    pivotRotation = (selection.First() as Transform).rotation;
                }
                else
                {
                    pivotRotation = (selection.Except(oldSelection).Union(oldSelection.Except(selection)).First() as Transform).rotation;
                }
            }
            else
            {
                pivotRotation = (selection.First() as Transform).rotation;
            }
            pivotPosition = Vector3.zero;
            Vector3 up = Vector3.zero;
            foreach (var entry in selectedObjects)
            {
                entry.Transform.hideFlags = HideFlags.None;
                if (entry.Pivot)
                {
                    DestroyImmediate(entry.Pivot.gameObject);
                }
            }
            selectedObjects.Clear();
            foreach (var t in selection.Select(o => o as Transform))
            {
                pivotPosition += t.position;
                up += t.up;
            }
            pivotPosition /= selection.Count();
            pivot.position = pivotPosition;
            pivot.rotation = pivotRotation;
            foreach (var t in selection.Select(o => o as Transform))
            {
                selectedObjects.Add(new TransformEntry(t, ref pivot));
                t.hideFlags = HideFlags.NotEditable;
            }
            pivotScale = Vector3.one;
        }

        private FieldInfo LastControlIdField = typeof(EditorGUIUtility).GetField("s_LastControlID", BindingFlags.Static | BindingFlags.NonPublic);
        private int GetLastControlId()
        {
            if (LastControlIdField == null)
            {
                Debug.LogError("Compatibility with Unity broke: can't find lastControlId field in EditorGUI");
                return 0;
            }
            return (int)LastControlIdField.GetValue(null);
        }

        private void AdjustPivot(bool enabled)
        {
            adjustPivot = enabled;
            if (!adjustPivot)
            {
                return;
            }
            foreach (var entry in selectedObjects)
            {
                entry.Position = entry.Pivot.position;
                entry.Rotation = entry.Pivot.rotation;
            }
        }

        private void SnapPivot(bool enabled)
        {
            snapPivot = enabled;
            if (!snapPivot)
            {
                return;
            }
        }

        private Edge[] GetMeshEdges(MeshFilter mf)
        {
            Mesh mesh = mf.sharedMesh;
            HashSet<Edge> edges = new HashSet<Edge>();

            for (int i = 0; i < mesh.triangles.Length; i += 3)
            {
                var v0 = mf.transform.TransformPoint(mesh.vertices[mesh.triangles[i]]);
                var v1 = mf.transform.TransformPoint(mesh.vertices[mesh.triangles[i + 1]]);
                var v2 = mf.transform.TransformPoint(mesh.vertices[mesh.triangles[i + 2]]);
                var n0 = mf.transform.TransformDirection(mesh.normals[mesh.triangles[i]] + mesh.normals[mesh.triangles[i + 1]]).normalized;
                var n1 = mf.transform.TransformDirection(mesh.normals[mesh.triangles[i]] + mesh.normals[mesh.triangles[i + 2]]).normalized;
                var n2 = mf.transform.TransformDirection(mesh.normals[mesh.triangles[i + 1]] + mesh.normals[mesh.triangles[i + 2]]).normalized;
                edges.Add(new Edge(v0, v1, n0));
                edges.Add(new Edge(v0, v2, n1));
                edges.Add(new Edge(v1, v2, n2));
            }

            return edges.ToArray();
        }

        private Edge[] GetMeshEdges(SpriteRenderer sr)
        {
            HashSet<Edge> edges = new HashSet<Edge>();

            // Getting the triangulated sprite edges doesn't feel as useful after trying it out
            // for (int i = 0; i < sr.sprite.triangles.Length; i += 3)
            // {
            //     var v0 = sr.transform.TransformPoint(sr.sprite.vertices[sr.sprite.triangles[i]]);
            //     var v1 = sr.transform.TransformPoint(sr.sprite.vertices[sr.sprite.triangles[i + 1]]);
            //     var v2 = sr.transform.TransformPoint(sr.sprite.vertices[sr.sprite.triangles[i + 2]]);
            //     edges.Add(new Edge(v0, v1, -view.camera.transform.forward));
            //     edges.Add(new Edge(v0, v2, -view.camera.transform.forward));
            //     edges.Add(new Edge(v1, v2, -view.camera.transform.forward));
            // }

            // Get the sprite's rect instead
            Rect r = sr.sprite.rect;
            Vector2 size = sr.transform.TransformPoint(r.size) / sr.sprite.pixelsPerUnit;
            r.position = sr.transform.position;
            r.position -= size / 2;
            r.size = size;
            float z = sr.transform.position.z;
            Vector3 n = -view.camera.transform.forward;
            Vector3 v0 = r.min;
            v0.z = z;
            Vector3 v1 = new Vector3(r.xMax, r.yMin, z);
            Vector3 v2 = r.max;
            v2.z = z;
            Vector3 v3 = new Vector3(r.xMin, r.yMax, z);
            edges.Add(new Edge(v0, v1, n));
            edges.Add(new Edge(v1, v2, n));
            edges.Add(new Edge(v2, v3, n));
            edges.Add(new Edge(v3, v0, n));
            edges.Add(new Edge(v0, v2, n));
            edges.Add(new Edge(v1, v3, n));

            return edges.ToArray();
        }


        private bool Raycast(bool forceRefresh = false)
        {
            // TODO: need a more robust way to do this for 2D stuff
            // Do SpriteShape, etc derive from SpriteRenderer? Probably not?
            GameObject go = HandleUtility.PickGameObject(e.mousePosition, false);
            if (!go && currentSpriteRenderer)
            {
                Vector2 mousePosition = e.mousePosition;
                mousePosition.y = view.camera.pixelHeight - mousePosition.y;
                Vector3 p = view.camera.ScreenToWorldPoint(mousePosition * EditorGUIUtility.pixelsPerPoint);
                p.z = currentSpriteRenderer.transform.position.z;
                Rect r = currentSpriteRenderer.sprite.rect;
                Vector2 size = currentSpriteRenderer.transform.TransformPoint(r.size) / currentSpriteRenderer.sprite.pixelsPerUnit;
                r.position = currentSpriteRenderer.transform.position;
                r.position -= size / 2;
                r.size = size;
                if (r.Contains(p))
                {
                    go = currentSpriteRenderer.gameObject;
                }
            }
            if (go)
            {
                MeshFilter mf = go.GetComponent<MeshFilter>();
                if (mf)
                {
                    Vector2 mousePosition = view.camera.ScreenToViewportPoint(e.mousePosition * EditorGUIUtility.pixelsPerPoint);
                    mousePosition.y = 1 - mousePosition.y;
                    ray = view.camera.ViewportPointToRay(mousePosition);
                    if (MeshRaycast.IntersectRayMesh(ray, mf, out hit))
                    {
                        if (currentMeshFilter != mf || forceRefresh)
                        {
                            currentSpriteRenderer = null;
                            currentMeshFilter = mf;
                            currentEdges = GetMeshEdges(currentMeshFilter);
                        }
                        return true;
                    }
                }
                SpriteRenderer sr = go.GetComponent<SpriteRenderer>();
                if (sr && view.in2DMode)
                {
                    Vector2 mousePosition = view.camera.ScreenToViewportPoint(e.mousePosition * EditorGUIUtility.pixelsPerPoint);
                    mousePosition.y = 1 - mousePosition.y;
                    ray = view.camera.ViewportPointToRay(mousePosition);
                    hit = new RaycastHit();
                    float d = sr.transform.position.z - view.camera.transform.position.z;
                    hit.point = ray.GetPoint(d);
                    hit.distance = Mathf.Abs(d);
                    currentMeshFilter = null;
                    currentSpriteRenderer = sr;
                    currentEdges = GetMeshEdges(sr);
                    return true;
                }
            }
            hit = new RaycastHit();
            currentMeshFilter = null;
            currentEdges = null;
            return false;
        }

        public override void OnToolGUI(EditorWindow window)
        {
            if (toggleButton == null)
            {
                toggleButton = new GUIStyle(GUI.skin.button);
                toggleButton.alignment = TextAnchor.MiddleRight;
            }
            view = window as SceneView;
            e = Event.current;
            // selection.First() != null
            // Gets around a specific situation with undo
            // Object selected, tool active, deselect object without deactivating tool
            // Reselect same object, do something, undo
            // selection will have a null object in place of the actual object reference for a single cycle
            //
            // selection == null
            // Fixes an issue if the tool is active when a domain reload happens
            //
            // Need this bit since we don't have an OnSelectionChange callback
            if (selection == null || (targets != selection && selection.First() != null))
            {
                OnSelectionChange();
            }
            if (e.type == EventType.KeyDown)
            {
                // Prevent object deletion, really only because of the following error:
                // "Nested object batch deleting detected!"
                if (e.keyCode == deleteKeycode)
                {
                    e.Use();
                }
            }
            if ((!snapPivot && snapKey != KeyCode.None && e.type == EventType.KeyDown && e.keyCode == snapKey) ||
                (snapPivot && (e.type == EventType.MouseMove || e.type == EventType.MouseDrag)))
            {
                Raycast();
            }
            snapped = false;
            if (snapPivot)
            {
                // Ensure clicking in snap mode doesn't select the object under the pointer
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
                if (currentEdges != null)
                {
                    Vector3[] edgePoints = new Vector3[3];
                    Vector3 p = view.camera.WorldToScreenPoint(hit.point);
                    Vector3 p1 = p;
                    adjustedPoint = hit.point;
                    // Edge edge = currentEdges.Where(e => Vector3.Dot(e.n, ray.direction) < 0).OrderBy(e => HandleUtility.DistancePointLine(hit.point, e.v0, e.v1)).First();
                    Edge[] edges = currentEdges.Where(e => Vector3.Dot(e.n, ray.direction) < 0).OrderBy(e => HandleUtility.DistancePointLine(hit.point, e.v0, e.v1)).ToArray();
                    int max = Mathf.Min(edges.Length, 25);
                    for (int i = 1; i < max; i++)
                    {
                        if (HandleUtility.DistanceToLine(edges[i].v0, edges[i].v1) > snapTolerance)
                        {
                            continue;
                        }
                        Handles.zTest = UnityEngine.Rendering.CompareFunction.Less;
                        Color c = Handles.color;
                        Handles.color = Color.cyan;
                        Handles.DrawAAPolyLine(5, edges[i].v0, edges[i].v1);
                        Handles.color = c;
                        Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
                    }
                    if (edges.Length > 0)
                    {
                        float d = HandleUtility.DistancePointLine(hit.point, edges[0].v0, edges[0].v1);
                        edgePoints[0] = edges[0].v0;
                        edgePoints[1] = (edges[0].v0 + edges[0].v1) / 2;
                        edgePoints[2] = edges[0].v1;
                        Vector3 cp = edgePoints.OrderBy(v => (view.camera.WorldToScreenPoint(v) - p).sqrMagnitude).First();
                        if ((view.camera.WorldToScreenPoint(cp) - p).sqrMagnitude < snapTolerance * snapTolerance)
                        // if (HandleUtility.DistanceToCircle(cp, 0) < snapTolerance)
                        {
                            p1 = cp;
                        }
                        else
                        {
                            p1 = HandleUtility.ProjectPointLine(hit.point, edges[0].v0, edges[0].v1);
                        }
                        if (d < 0.1f)
                        {
                            // if (HandleUtility.DistanceToCircle(cp, 0) < snapTolerance)
                            if ((view.camera.WorldToScreenPoint(p1) - p).sqrMagnitude < snapTolerance * snapTolerance)
                            {
                                Handles.zTest = UnityEngine.Rendering.CompareFunction.Less;
                                Color c = Handles.color;
                                Handles.color = Color.cyan;
                                Handles.DrawAAPolyLine(5, edges[0].v0, edges[0].v1);
                                Handles.color = c;
                                Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
                                adjustedPoint = p1;
                                snapped = true;
                            }
                        }
                    }
                }
            }
            if (snapPivot && e.type == EventType.MouseUp && e.button == 0)
            {
                if (Raycast())
                {
                    Undo.RecordObjects(selectedObjects.Select(o => o.Transform).ToArray(), "Move");
                    Undo.RecordObjects(selectedObjects.Select(o => o.Pivot).ToArray(), "Move");
                    Undo.RecordObject(pivot, "Move");
                    pivot.position = pivotPosition = adjustedPoint;
                    foreach (var entry in selectedObjects)
                    {
                        if (adjustPivot)
                        {
                            entry.Pivot.position = entry.Position;
                        }
                        else
                        {
                            entry.Transform.position = entry.Pivot.position;
                            Raycast(forceRefresh: true);
                        }
                    }
                    return;
                }
            }
            if (snapPivot && hit.distance > 0)
            {
                if (e.type == EventType.Repaint)
                {
                    Color c = Handles.color;
                    Handles.CubeHandleCap(0, pivot.position, pivot.rotation, HandleUtility.GetHandleSize(pivot.position) * 0.1f, EventType.Repaint);
                    Handles.color = Handles.xAxisColor;
                    Handles.DrawLine(pivot.position, pivot.position + (pivot.right * HandleUtility.GetHandleSize(pivot.position)));
                    Handles.color = Handles.yAxisColor;
                    Handles.DrawLine(pivot.position, pivot.position + (pivot.up * HandleUtility.GetHandleSize(pivot.position)));
                    Handles.color = Handles.zAxisColor;
                    Handles.DrawLine(pivot.position, pivot.position + (pivot.forward * HandleUtility.GetHandleSize(pivot.position)));
                    Handles.color = (snapped ? Color.green : Color.white) * 0.9f;
                    Handles.SphereHandleCap(0, adjustedPoint, Quaternion.identity, HandleUtility.GetHandleSize(adjustedPoint) * 0.1f, EventType.Repaint);
                    Handles.color = Color.white * 0.5f;
                    Handles.SphereHandleCap(0, adjustedPoint, Quaternion.identity, view.camera.farClipPlane / 100000, EventType.Repaint);
                    Handles.color = c;
                }
            }
            else
            {
                if (adjustPivot)
                {
                    Handles.TransformHandle(ref pivotPosition, ref pivotRotation);
                }
                else
                {
                    Handles.TransformHandle(ref pivotPosition, ref pivotRotation, ref pivotScale);
                }
            }
            if (EditorGUIUtility.hotControl != 0)
            {
                int handleId = EditorGUIUtility.hotControl + 15 - GetLastControlId();
                // Debug.Log(handleId);
                switch (handleId)
                {
                    case 0:
                    case 1:
                    case 2:
                    case 3:
                    case 4:
                    case 5:
                    case 6:
                        // Move handles
                        Undo.RecordObjects(selectedObjects.Select(o => o.Transform).ToArray(), "Move");
                        Undo.RecordObjects(selectedObjects.Select(o => o.Pivot).ToArray(), "Move");
                        Undo.RecordObject(pivot, "Move");
                        pivot.position = pivotPosition;
                        foreach (var entry in selectedObjects)
                        {
                            if (adjustPivot)
                            {
                                entry.Pivot.position = entry.Position;
                            }
                            else
                            {
                                entry.Transform.position = entry.Pivot.position;
                            }
                        }
                        break;
                    case 7:
                    case 8:
                    case 9:
                    case 10:
                    case 11:
                        // Rotation handles
                        Undo.RecordObjects(selectedObjects.Select(o => o.Transform).ToArray(), "Rotate");
                        Undo.RecordObjects(selectedObjects.Select(o => o.Pivot).ToArray(), "Rotate");
                        Undo.RecordObject(pivot, "Rotate");
                        pivot.rotation = pivotRotation;
                        foreach (var entry in selectedObjects)
                        {
                            if (adjustPivot)
                            {
                                entry.Pivot.position = entry.Position;
                                entry.Pivot.rotation = entry.Rotation;
                            }
                            else
                            {
                                entry.Transform.position = entry.Pivot.position;
                                entry.Transform.rotation = entry.Pivot.rotation;
                            }
                        }
                        break;
                    case 12:
                    case 13:
                    case 14:
                    case 15:
                        // Scale handles
                        if (adjustPivot)
                        {
                            break;
                        }
                        Undo.RecordObjects(selectedObjects.Select(o => o.Transform).ToArray(), "Scale");
                        Undo.RecordObjects(selectedObjects.Select(o => o.Pivot).ToArray(), "Scale");
                        Undo.RecordObject(pivot, "Scale");
                        pivot.localScale = pivotScale;
                        foreach (var entry in selectedObjects)
                        {
                            entry.Transform.position = entry.Pivot.position;
                            entry.Transform.localScale = entry.Pivot.lossyScale;
                        }
                        break;
                }
            }
            Handles.BeginGUI();
            adjustPivot = GUILayout.Toggle(adjustPivot, "[" + adjustKey.ToString() + "] Adjust\r\nPivot", toggleButton, GUILayout.Width(70), GUILayout.Height(40));
            snapPivot = GUILayout.Toggle(snapPivot, "[" + snapKey.ToString() + "] Snap\r\nPivot", toggleButton, GUILayout.Width(70), GUILayout.Height(40));
            GUILayout.Box("Snap\r\nTolerance", GUILayout.Width(70));
            snapTolerance = EditorGUILayout.FloatField(snapTolerance, GUILayout.Width(70));
            if (GUILayout.Button("Snap\r\nSettings", GUILayout.Width(70)))
            {
                var editorTypes = typeof(UnityEditor.Editor).Assembly.GetTypes();
                var snap = editorTypes.FirstOrDefault(t => t.Name == "SnapSettings");
                EditorWindow.GetWindow(snap, true);
            }
            Handles.EndGUI();
        }
    }
}