using System.IO;
using SpeechIntent;
using UnityEditor;
using UnityEngine;

namespace Holodeck.Editor
{
    public static class LcarsPrefabFactory
    {
        const string SpriteRoot = "Assets/App/UI/LCARS/Sprites/";
        const string PrefabRoot = "Assets/App/UI/LCARS/Prefabs";

        [MenuItem("Holodeck/Create LCARS Elbow Prefabs")]
        public static void CreatePrefabs()
        {
            Directory.CreateDirectory(PrefabRoot);
            AssetDatabase.Refresh();
            CreateOne("LCARS_TopLeftElbow.prefab", LcarsElbowGraphic.ElbowCorner.TopLeft);
            CreateOne("LCARS_TopRightElbow.prefab", LcarsElbowGraphic.ElbowCorner.TopRight);
            CreateOne("LCARS_BottomLeftElbow.prefab", LcarsElbowGraphic.ElbowCorner.BottomLeft);
            CreateOne("LCARS_BottomRightElbow.prefab", LcarsElbowGraphic.ElbowCorner.BottomRight);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[LcarsPrefabFactory] Created LCARS elbow prefabs.");
        }

        static void CreateOne(string fileName, LcarsElbowGraphic.ElbowCorner corner)
        {
            var go = new GameObject(Path.GetFileNameWithoutExtension(fileName), typeof(RectTransform));
            var graphic = go.AddComponent<LcarsElbowGraphic>();
            graphic.corner = corner;
            graphic.size = new Vector2(520f, 300f);
            graphic.horizontalThickness = 92f;
            graphic.verticalThickness = 92f;
            graphic.cornerSize = 92f;
            graphic.innerCutoutSize = 148f;
            graphic.outerTopLeft = Load("LCARS_OneCorner_TL_128.png");
            graphic.outerTopRight = Load("LCARS_OneCorner_TR_128.png");
            graphic.outerBottomLeft = Load("LCARS_OneCorner_BL_128.png");
            graphic.outerBottomRight = Load("LCARS_OneCorner_BR_128.png");
            graphic.inverseTopLeft = Load("LCARS_InnerCorner_TL_128.png");
            graphic.inverseTopRight = Load("LCARS_InnerCorner_TR_128.png");
            graphic.inverseBottomLeft = Load("LCARS_InnerCorner_BL_128.png");
            graphic.inverseBottomRight = Load("LCARS_InnerCorner_BR_128.png");
            graphic.Rebuild();

            PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabRoot}/{fileName}");
            Object.DestroyImmediate(go);
        }

        static Sprite Load(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(SpriteRoot + name);
    }
}
