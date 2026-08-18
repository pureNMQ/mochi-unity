using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using System;
using System.IO;
using System.Text.RegularExpressions;
using Mochi.Unity.Tags;

namespace Mochi.Unity.Editor
{
[CustomEditor(typeof(TagDictionarySO))]
public class TagDictionaryEditor : UnityEditor.Editor
{
    private VisualElement tagListView;
    private List<Foldout> tagFoldouts;

    private TextField addTagField;
    private Button addTagButton;

    private Button applyButton;

    private TagDictionarySO tagDictionary;

    public override VisualElement CreateInspectorGUI()
    {
        tagDictionary = target as TagDictionarySO;
        VisualElement root = new VisualElement();

        //标签列表
        tagListView = new VisualElement();
        tagListView.style.paddingLeft = 15f;
        tagListView.style.paddingRight = 10f;

        VisualElement tagListBorder = new VisualElement();
        tagListBorder.style.borderTopColor = Color.black;
        tagListBorder.style.borderBottomColor = Color.black;
        tagListBorder.style.borderRightColor = Color.black;
        tagListBorder.style.borderLeftColor = Color.black;
        tagListBorder.style.borderTopWidth = 1f;
        tagListBorder.style.borderBottomWidth = 1f;
        tagListBorder.style.borderRightWidth = 1f;
        tagListBorder.style.borderLeftWidth = 1f;
        tagListBorder.style.borderTopRightRadius = 5f;
        tagListBorder.style.borderTopLeftRadius = 5f;
        tagListBorder.style.borderBottomRightRadius = 5f;
        tagListBorder.style.borderBottomRightRadius = 5f;
        tagListBorder.style.marginTop = 10f;

        Label tagListViewTitle = new Label("标签列表");
        tagListViewTitle.style.unityTextAlign = TextAnchor.MiddleCenter;
        tagListViewTitle.style.fontSize = 15;
        tagListViewTitle.style.marginTop = 5f;
        tagListViewTitle.style.marginBottom = 5f;
        tagListViewTitle.style.borderBottomColor = Color.black;
        tagListViewTitle.style.borderBottomWidth = 1f;

        tagListBorder.Add(tagListViewTitle);
        tagListBorder.Add(tagListView);

        tagFoldouts = new List<Foldout>();

        UpdateTagListView();

        //添加标签界面
        VisualElement addTagView = new VisualElement();
        addTagView.style.flexDirection = FlexDirection.Column;

        addTagField = new TextField();
        addTagField.label = "标签名";
        addTagField.style.flexGrow = 1f;
        addTagField.RegisterCallback<KeyDownEvent>(OnAddTagFieldKeyDown);

        addTagButton = new Button(AddTag)
        {
            text = "添加"
        };

        applyButton = new Button(Apply)
        {
            text = "应用"
        };

        applyButton.style.height = 30f;
        applyButton.style.marginBottom = 10f;

        if (!tagDictionary.isApply)
        {
            NeedApply();
        }

        addTagView.Add(addTagField);
        addTagView.Add(addTagButton);
        root.Add(applyButton);

        root.Add(addTagView);
        root.Add(tagListBorder);

        Button ClearButton = new Button(() =>
        {
            tagDictionary.tagNames.Clear();
            EditorUtility.SetDirty(tagDictionary);
            UpdateTagListView();
        });

        ClearButton.text = "清空所有标签";
        //root.Add(ClearButton);

        Undo.undoRedoEvent += OnUndoRedo;

        return root;
    }

    private void Apply()
    {
        EditorUtility.SetDirty(tagDictionary);
        GenerateCode();
        AssetDatabase.SaveAssets();
        tagDictionary.isApply = true;
        applyButton.style.backgroundColor = default;
    }

    private void NeedApply()
    {
        applyButton.style.backgroundColor = new Color(1, 0.3f, 0.3f);
        tagDictionary.isApply = false;
    }


    //检测输入框的回车，添加标签
    private void OnAddTagFieldKeyDown(KeyDownEvent evt)
    {
        if (evt.keyCode == KeyCode.Return)
        {
            AddTag();
        }
    }

    private void OnUndoRedo(in UndoRedoInfo undo)
    {
        if (undo.undoName == "Remove Tag" || undo.undoName == "Add Tag")
        {
            UpdateTagListView();
        }
    }

    private void AddTag()
    {
        if (string.IsNullOrEmpty(addTagField.value)) return;
        string pattern = @"[^a-zA-Z0-9\.]";

        if (Regex.IsMatch(addTagField.value, pattern))
        {
            Debug.LogWarning("标签名只能包含字母、数字和'.'");
            return;
        }

        if (addTagField.value[0] >= '0' && addTagField.value[0] <= '9')
        {
            Debug.LogWarning("标签不能以数字开头");
            return;
        }

        if (addTagField.value.Contains("..") || addTagField.value[0] == '.' || addTagField.value[^1] == '.')
        {
            Debug.LogWarning("标签名不能包含'..'或以'.'开头或结尾");
            return;
        }



        Undo.RecordObject(tagDictionary, "Add Tag");

        tagDictionary.AddTag(addTagField.value);

        addTagField.value = "";

        EditorUtility.SetDirty(tagDictionary);
        UpdateTagListView();
        NeedApply();
    }


    private void UpdateTagListView()
    {
        tagListView.Clear();
        List<Foldout> oldTagFoldouts = new List<Foldout>(tagFoldouts);
        tagFoldouts.Clear();

        for (int i = 0; i < tagDictionary.tagNames.Count; i++)
        {
            Foldout foldout = new Foldout();
            string tag = tagDictionary.tagNames[i];
            foldout.name = tag;
            int lastIndex = tag.LastIndexOf('.');
            if (lastIndex > 0)
            {
                foldout.text = tag.Substring(lastIndex + 1);
                string parentTag = tag.Substring(0, lastIndex);
                Foldout parentFoldout = tagFoldouts.Find(x => x.name == parentTag);
                parentFoldout.Add(foldout);
                parentFoldout.Q<VisualElement>("unity-checkmark").visible = true;
            }
            else
            {
                foldout.text = tag;
                tagListView.Add(foldout);
            }

            foldout.Q<VisualElement>("unity-checkmark").visible = false;

            Foldout oldFoldouts = oldTagFoldouts.Find(x => x.name == tag);
            if (oldFoldouts is not null)
            {
                foldout.value = oldFoldouts.value;
            }
            else
            {
                foldout.value = false;
            }
            VisualElement title = foldout.Q<Toggle>();

            //防止名称过长而挤压按钮位置
            title.ElementAt(0).style.flexShrink = 1;

            Button addButton = new Button(() =>
            {
                addTagField.value = tag + ".";
            });
            addButton.text = "  +";
            title.Add(addButton);

            Button removeButton = new Button(() =>
            {
                Undo.RecordObject(tagDictionary, "Remove Tag");
                tagDictionary.RemoveTag(tag);
                foldout.parent.Remove(foldout);
                NeedApply();
            });
            removeButton.text = " -";
            title.Add(removeButton);

            tagFoldouts.Add(foldout);
        }
    }


    private void GenerateCode()
    {
        string filepath = Path.Combine(Application.dataPath, "Scripts", "Generated", "TagDictionary");
        if (!Directory.Exists(filepath))
        {
            Directory.CreateDirectory(filepath);
        }

        filepath = Path.Combine(filepath, "Tag.cs");

        StreamWriter sw = new StreamWriter(filepath);

        sw.WriteLine("public static class Tag\n{");
        for (int i = 0; i < tagDictionary.tagNames.Count; i++)
        {
            sw.WriteLine($"\tpublic const int {tagDictionary.tagNames[i].Replace('.', '_')} = {i};");
        }

        sw.WriteLine("}");
        sw.Close();

        AssetDatabase.Refresh();

    }
}
}
