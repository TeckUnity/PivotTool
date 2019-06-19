using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine.Rendering;
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
        private static string packagePath;// = "Packages/com.ltk.pivot/";
        private static PivotTool instance;

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
            instance = this;
            Undo.undoRedoPerformed += UndoCallback;
            pivot = new GameObject("Pivot").transform;
            pivot.gameObject.hideFlags = HideFlags.HideAndDontSave;

            // Was going to use this to be a bit flexible about delete detection, but switch/case requires constants
            // var deleteBinding = ShortcutManager.instance.GetShortcutBinding("Main Menu/Edit/Delete").keyCombinationSequence;
            // if (deleteBinding.Count() > 0)
            // {
            //     deleteKeycode = deleteBinding.First().keyCode;
            // }
            // else
            // {
            //     deleteKeycode = KeyCode.Delete;
            // }

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

        private bool Raycast(out RaycastHit hit)
        {
            GameObject g = HandleUtility.PickGameObject(e.mousePosition, false);
            if (g)
            {
                MeshFilter mf = g.GetComponent<MeshFilter>();
                if (mf)
                {
                    Vector2 mousePosition = view.camera.ScreenToViewportPoint(e.mousePosition * EditorGUIUtility.pixelsPerPoint);
                    mousePosition.y = 1 - mousePosition.y;
                    ray = view.camera.ViewportPointToRay(mousePosition);
                    if (MeshRaycast.IntersectRayMesh(ray, mf, out hit))
                    {
                        return true;
                    }
                }
            }
            hit = new RaycastHit();
            return false;
        }

        public override void OnToolGUI(EditorWindow window)
        {
            view = window as SceneView;
            e = Event.current;
            if (targets != selection)
            {
                OnSelectionChange();
            }
            if (e.type == EventType.KeyDown)
            {
                switch (e.keyCode)
                {
                    case KeyCode.Delete:
                        // Prevent object deletion, really only because of the following error:
                        // "Nested object batch deleting detected!"
                        e.Use();
                        break;
                }
            }
            if (snapPivot)
            {
                // Ensure clicking in snap mode doesn't select the object under the pointer
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            }
            // if (snapPivot && pivotPreview != Vector3.positiveInfinity)
            // {
            //     Handles.SphereHandleCap(0, pivotPreview, Quaternion.identity, 0.1f, EventType.Repaint);
            // }
            // if (snapPivot && e.type == EventType.MouseMove)
            // {
            //     if (Raycast(out hit))
            //     {
            //         pivotPreview = hit.point;
            //     }
            //     return;
            // }
            if (snapPivot && e.type == EventType.MouseUp && e.button == 0)
            {
                if (Raycast(out hit))
                {
                    Undo.RecordObjects(selectedObjects.Select(o => o.Transform).ToArray(), "Move");
                    Undo.RecordObjects(selectedObjects.Select(o => o.Pivot).ToArray(), "Move");
                    Undo.RecordObject(pivot, "Move");
                    foreach (var entry in selectedObjects)
                    {
                        entry.Position = entry.Pivot.position;
                    }
                    pivot.position = pivotPosition = hit.point;
                    foreach (var entry in selectedObjects)
                    {
                        entry.Pivot.position = entry.Position;
                        entry.Transform.position = entry.Position;
                    }
                    return;
                }
            }
            Handles.TransformHandle(ref pivotPosition, ref pivotRotation, ref pivotScale);
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
            adjustPivot = GUILayout.Toggle(adjustPivot, "Adjust\r\nPivot", GUI.skin.button, GUILayout.Width(60), GUILayout.Height(40));
            snapPivot = GUILayout.Toggle(snapPivot, "Snap\r\nPivot", GUI.skin.button, GUILayout.Width(60), GUILayout.Height(40));
            Handles.EndGUI();
        }
    }
}