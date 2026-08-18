using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using System;
using System.Linq;
using Mochi.Unity.Tags;

namespace Mochi.Unity.Editor
{
[CustomPropertyDrawer(typeof(TagContainer))]
public class TagContainerPropertyDrawer : PropertyDrawer
{
    public class EditorTagWindow : PopupWindowContent
    {
        private List<Foldout> tagFoldouts;
        private List<Toggle> tagToggles;
        private VisualElement root;
        private ScrollView tagListView;
        private SerializedProperty serializedProperty;
        private SerializedProperty presetTagRelative;
        private SerializedProperty runtimeTagRelative;
        private List<string> presetTags;
        private List<int> runtimeTagIndex;
        private TagContainerPropertyDrawer drawer;
        private Vector2 windowSize;

        public EditorTagWindow(SerializedProperty property, TagContainerPropertyDrawer drawer, Vector2 size)
        {
            serializedProperty = property;
            this.drawer = drawer;
            windowSize = size;
            presetTags = new List<string>();
            runtimeTagIndex = new List<int>();
            presetTagRelative = property.FindPropertyRelative(nameof(presetTags));
            runtimeTagRelative = property.FindPropertyRelative("runtimeTags");

            for (int i = 0; i < presetTagRelative.arraySize; i++)
            {
                string tagName = presetTagRelative.GetArrayElementAtIndex(i).stringValue;
                presetTags.Add(tagName);
                int tagIndex = TagManager.Instance.GetTagIndexByName(tagName);
                if (tagIndex >= 0)
                {
                    runtimeTagIndex.Add(tagIndex);
                }
            }
        }
        public override void OnOpen()
        {
            if (root == null)
            {
                root = new VisualElement();
            }
            UpdateTagListView();
            editorWindow.rootVisualElement.Add(root);
        }

        public override Vector2 GetWindowSize()
        {
            return windowSize;
        }

        private void UpdateTagListView()
        {
            if (tagFoldouts == null)
            {
                CreateTagListView();
            }
            if (Application.isPlaying)
            {

            }
            for (int i = 0; i < tagFoldouts.Count; i++)
            {
                var tagToggle = tagToggles[i];
                if (TagManager.Instance.HasTag(runtimeTagIndex, i))
                {
                    tagToggle.SetValueWithoutNotify(true);
                    if (TagManager.Instance.HasTagExact(runtimeTagIndex, i))
                    {
                        tagToggle.style.opacity = 1f;
                    }
                    else
                    {
                        tagToggle.style.opacity = 0.2f;
                    }
                }
                else
                {
                    tagToggle.SetValueWithoutNotify(false);
                    tagToggle.style.opacity = 1f;
                }
            }

        }

        private void CreateTagListView()
        {
            tagFoldouts = new List<Foldout>();
            tagToggles = new List<Toggle>();
            tagListView = new ScrollView();

            foreach (var item in TagManager.Instance.TagDefines)
            {
                var foldout = new Foldout();
                foldout.name = item.name;
                int lastIndex = item.name.LastIndexOf('.');
                if (lastIndex > 0)
                {
                    foldout.text = item.name.Substring(lastIndex + 1);
                    string parentTag = item.name.Substring(0, lastIndex);
                    Foldout parentFoldout = tagFoldouts.Find(x => x.name == parentTag);
                    parentFoldout.Add(foldout);
                    parentFoldout.Q<VisualElement>("unity-checkmark").visible = true;
                }
                else
                {
                    foldout.text = item.name;
                    tagListView.contentContainer.Add(foldout);

                }

                foldout.value = false;
                Toggle tagToggle = new Toggle();
                tagToggle.RegisterValueChangedCallback(evt => OnTagToggleValueChange(evt.newValue, item));
                tagToggles.Add(tagToggle);

                foldout.Q<Toggle>().ElementAt(0).Insert(1, tagToggle);
                foldout.Q<VisualElement>("unity-checkmark").visible = false;

                tagFoldouts.Add(foldout);

                root.Add(tagListView);
            }
        }
        private void OnTagToggleValueChange(bool value, TagDefine tagDefine)
        {
            //更新runtimeTagIndex
            if (Application.isPlaying)
            {
                runtimeTagIndex.Clear();
                for (int i = 0; i < runtimeTagRelative.arraySize; i++)
                {
                    runtimeTagIndex.Add(runtimeTagRelative.GetArrayElementAtIndex(i).intValue);
                }
            }

            bool hasTag = TagManager.Instance.HasTag(runtimeTagIndex, tagDefine.index);
            bool hasTagExact = TagManager.Instance.HasTagExact(runtimeTagIndex, tagDefine.index);

            if (value && !hasTagExact || !value && hasTag && !hasTagExact)
            {
                presetTags.Add(tagDefine.name);
                presetTags = presetTags.OrderBy(x => x).ToList();
                runtimeTagIndex.Add(tagDefine.index);
                runtimeTagIndex = runtimeTagIndex.OrderBy(x => x).ToList();
            }
            else if (!value && hasTagExact)
            {
                presetTags.Remove(tagDefine.name);
                runtimeTagIndex.Remove(tagDefine.index);
            }

            if (Application.isPlaying)
            {
                runtimeTagRelative.ClearArray();
                foreach (var item in runtimeTagIndex)
                {
                    runtimeTagRelative.InsertArrayElementAtIndex(runtimeTagRelative.arraySize);
                    runtimeTagRelative.GetArrayElementAtIndex(runtimeTagRelative.arraySize - 1).intValue = item;
                }
                runtimeTagRelative.serializedObject.ApplyModifiedProperties();
            }
            else
            {
                presetTagRelative.ClearArray();
                foreach (var item in presetTags)
                {
                    presetTagRelative.InsertArrayElementAtIndex(presetTagRelative.arraySize);
                    presetTagRelative.GetArrayElementAtIndex(presetTagRelative.arraySize - 1).stringValue = item;
                }
                presetTagRelative.serializedObject.ApplyModifiedProperties();
                UpdateTagListView();
            }

            drawer.UpdateTagList();
        }
    }

    private Button editorTagButton;
    private VisualElement tagLabelListView;
    private SerializedProperty property;
    public override VisualElement CreatePropertyGUI(SerializedProperty property)
    {
        this.property = property;
        VisualElement root = new VisualElement();

        tagLabelListView = new VisualElement();
        tagLabelListView.style.flexDirection = FlexDirection.Row;
        tagLabelListView.style.flexWrap = Wrap.Wrap;
        tagLabelListView.style.paddingRight = 5f;
        tagLabelListView.style.paddingLeft = 5f;
        //tagListView.style.paddingBottom = 5f;
        tagLabelListView.style.paddingTop = 5f;
        UpdateTagList();

        editorTagButton = new Button(PopupEditorTagWindow);
        editorTagButton.style.width = 75;
        editorTagButton.style.flexDirection = FlexDirection.Row;
        editorTagButton.Add(new Label("编辑标签"));
        VisualElement dropdownIcon = new VisualElement();
        dropdownIcon.style.backgroundImage = EditorGUIUtility.FindTexture("icon dropdown");
        dropdownIcon.style.width = 10;
        editorTagButton.Add(dropdownIcon);

        root.Add(tagLabelListView);
        root.Add(editorTagButton);

        return root;
    }

    public void UpdateTagList()
    {
        tagLabelListView.Clear();
        if (Application.isPlaying)
        {
            var runtimeTagRelative = property.FindPropertyRelative("runtimeTags");
            for (int i = 0; i < runtimeTagRelative.arraySize; i++)
            {
                var tag = runtimeTagRelative.GetArrayElementAtIndex(i);
                var tagLabel = CreateTagLabel(TagManager.Instance.GetTagNameByIndex(tag.intValue));
                tagLabelListView.Add(tagLabel);
            }
        }
        else
        {
            var presetTags = property.FindPropertyRelative("presetTags");
            for (int i = 0; i < presetTags.arraySize; i++)
            {
                var tag = presetTags.GetArrayElementAtIndex(i);
                var tagLabel = CreateTagLabel(tag.stringValue);
                tagLabelListView.Add(tagLabel);
            }
        }
    }

    private VisualElement CreateTagLabel(string tagName)
    {
        var tagLabel = new VisualElement();
        tagLabel.style.flexDirection = FlexDirection.Row;

        Label label = new Label(tagName);
        label.style.unityTextAlign = TextAnchor.MiddleCenter;
        tagLabel.Add(label);


        Button removeTagButton = new Button(() =>
        {
            if (Application.isPlaying)
            {
                var runtimeTagRelative = property.FindPropertyRelative("runtimeTags");
                for (int i = 0; i < runtimeTagRelative.arraySize; i++)
                {
                    if (runtimeTagRelative.GetArrayElementAtIndex(i).intValue == TagManager.Instance.GetTagIndexByName(tagName))
                    {
                        runtimeTagRelative.DeleteArrayElementAtIndex(i);
                    }
                    runtimeTagRelative.serializedObject.ApplyModifiedProperties();
                }
            }
            else
            {
                var presetTags = property.FindPropertyRelative("presetTags");
                for (int i = 0; i < presetTags.arraySize; i++)
                {
                    if (presetTags.GetArrayElementAtIndex(i).stringValue == tagName)
                    {
                        presetTags.DeleteArrayElementAtIndex(i);
                    }
                    presetTags.serializedObject.ApplyModifiedProperties();
                }

            }

            tagLabel.parent.Remove(tagLabel);
        });

        removeTagButton.text = "-";
        removeTagButton.style.height = 15f;
        removeTagButton.style.width = 10f;

        tagLabel.Add(removeTagButton);

        if (TagManager.Instance.HasTagDefine(tagName))
        {
            tagLabel.style.backgroundColor = Color.gray;
        }
        else
        {
            tagLabel.style.backgroundColor = Color.red;
        }
        tagLabel.style.borderTopLeftRadius = 3f;
        tagLabel.style.borderTopRightRadius = 3f;
        tagLabel.style.borderBottomLeftRadius = 3f;
        tagLabel.style.borderBottomRightRadius = 3f;
        tagLabel.style.marginBottom = 5f;
        tagLabel.style.marginRight = 5f;
        return tagLabel;
    }

    private void PopupEditorTagWindow()
    {
        UpdateTagList();
        Vector2 windowSize = new Vector2(tagLabelListView.contentRect.width, 300f);
        EditorTagWindow editorTagWindow = new EditorTagWindow(property, this, windowSize);

        Vector3 worldPosition = editorTagButton.worldTransform.GetPosition();
        float y = worldPosition.y + editorTagButton.contentRect.height + 10f;

        Rect activeRect = new Rect(worldPosition.x, y, 0, 0);
        UnityEditor.PopupWindow.Show(activeRect, editorTagWindow);
    }
}
}




