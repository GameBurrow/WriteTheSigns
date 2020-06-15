﻿using ColossalFramework;
using ColossalFramework.Threading;
using ColossalFramework.UI;
using Klyte.Commons.UI.Sprites;
using Klyte.Commons.Utils;
using SpriteFontPlus;
using SpriteFontPlus.Utility;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static ColossalFramework.UI.UITextureAtlas;

namespace Klyte.WriteTheSigns.Rendering
{
    public class WTSTransportLineRenderingRules : MonoBehaviour
    {
        public void Awake()
        {
            ResetAtlas();

            TransportManager.instance.eventLineColorChanged += PurgeLine;
            TransportManager.instance.eventLineNameChanged += PurgeLine;
        }

        private void ResetAtlas()
        {
            m_referenceAtlas = ScriptableObject.CreateInstance<UITextureAtlas>();
            m_referenceAtlas.material = new Material(Shader.Find("Custom/Props/Prop/Default"));
        }

        private UITextureAtlas m_referenceAtlas;

        public void PurgeLine(ushort lineId)
        {
            string id = $"{lineId}";
            if (m_referenceAtlas[id] != null)
            {
                m_referenceAtlas.Remove(id);
            }
            m_textCache.Remove(lineId);
            IsDirty = true;
        }
        public void PurgeAllLines()
        {
            m_textCache.Clear();
            ResetAtlas();
            IsDirty = true;
        }

        public Material Material => m_referenceAtlas.material;

        public bool IsDirty { get; private set; } = false;
        public LineIconSpriteNames LineIconTest
        {
            get => m_lineIconTest; set {
                m_lineIconTest = value;
                PurgeLine(0);
            }
        }

        public void UpdateMaterial()
        {
            if (IsDirty)
            {
                var aciTex = new Texture2D(m_referenceAtlas.texture.width, m_referenceAtlas.texture.height);
                aciTex.SetPixels(m_referenceAtlas.texture.GetPixels().Select(x => new Color(1 - x.a, 0, 1f, 1)).ToArray());
                aciTex.Apply();
                Material.SetTexture("_ACIMap", aciTex);

                IsDirty = false;
            }
        }

        private readonly Dictionary<ushort, TransportLineCacheItem> m_textCache = new Dictionary<ushort, TransportLineCacheItem>();


        private LineIconSpriteNames m_lineIconTest = LineIconSpriteNames.K45_HexagonIcon;

        public List<BasicRenderInformation> DrawLineFormats(IEnumerable<ushort> ids, Vector3 scale)
        {
            var bris = new List<BasicRenderInformation>();
            if (ids.Count() == 0)
            {
                return bris;
            }

            foreach (ushort id in ids.OrderBy(x => WriteTheSignsMod.Controller.ConnectorTLM.GetLineSortString(x)))
            {
                if (m_textCache.TryGetValue(id, out TransportLineCacheItem bri))
                {
                    if (bri?.m_logoData != null)
                    {
                        bris.Add(bri.m_logoData);
                    }
                }
                else
                {
                    if (!m_textCache.ContainsKey(id))
                    {
                        m_textCache[id] = new TransportLineCacheItem();
                    }

                    StartCoroutine(WriteTextureCoroutine(id, scale));
                }
            }
            return bris;

        }

        private IEnumerator WriteTextureCoroutine(ushort lineId, Vector3 scale)
        {
            yield return 0;
            string id = $"{lineId}";

            if (m_referenceAtlas[id] == null)
            {
                Tuple<string, Color, string> lineParams;
                if (lineId == 0)
                {
                    lineParams = Tuple.New(KlyteResourceLoader.GetDefaultSpriteNameFor(LineIconTest), (Color)ColorExtensions.FromRGB(0x5e35b1), "K");
                }
                else
                {
                    lineParams = WriteTheSignsMod.Controller.ConnectorTLM.GetLineLogoParameters(lineId);
                }
                var drawingCoroutine = CoroutineWithData.From(this, RenderSpriteLine(FontServer.instance[WTSController.DEFAULT_FONT_KEY], UIView.GetAView().defaultAtlas, lineParams.First, lineParams.Second, lineParams.Third));
                yield return drawingCoroutine.Coroutine;

                TextureAtlasUtils.RegenerateTextureAtlas(m_referenceAtlas, new List<UITextureAtlas.SpriteInfo>
                {
                    new UITextureAtlas.SpriteInfo
                    {
                        name = id,
                        texture = drawingCoroutine.result
                    }
                });
                IsDirty = true;
                m_textCache.Clear();
                StopAllCoroutines();
                yield break;
            }
            yield return 0;
            var bri = new BasicRenderInformation
            {
                m_YAxisOverflows = new RangeVector { min = 0, max = 20 },
            };

            yield return 0;
            var uirenderData = UIRenderData.Obtain();
            try
            {
                uirenderData.Clear();
                PoolList<Vector3> vertices = uirenderData.vertices;
                PoolList<Vector3> normals = uirenderData.normals;
                PoolList<Color32> colors = uirenderData.colors;
                PoolList<Vector2> uvs = uirenderData.uvs;
                PoolList<int> triangles = uirenderData.triangles;

                SpriteInfo spriteInfo = m_referenceAtlas[id];

                triangles.EnsureCapacity(triangles.Count + kTriangleIndices.Length);
                triangles.AddRange(kTriangleIndices);

                int baseIndex = 0;
                float x = 0f;
                float y = 0f;
                float x2 = 64;
                float y2 = -64;
                vertices.Add(new Vector3(x, y2, 0f));
                vertices.Add(new Vector3(x2, y2, 0f));
                vertices.Add(new Vector3(x2, y, 0f));
                vertices.Add(new Vector3(x, y, 0f));

                Rect region = spriteInfo.region;
                uvs.Add(new Vector2(region.xMax, region.y));
                uvs.Add(new Vector2(region.x, region.y));
                uvs.Add(new Vector2(region.x, region.yMax));
                uvs.Add(new Vector2(region.xMax, region.yMax));
                Vector2 value = Vector2.zero;

                for (int i = 0; i < 4; i++)
                {
                    colors.Add(Color.yellow);
                }

                if (bri.m_mesh == null)
                {
                    bri.m_mesh = new Mesh();
                }
                bri.m_YAxisOverflows.min *= scale.y;
                bri.m_YAxisOverflows.max *= scale.y;
                bri.m_mesh.Clear();
                bri.m_mesh.vertices = AlignVertices(vertices);
                bri.m_mesh.normals = normals.ToArray();
                bri.m_mesh.colors32 = colors.Select(x => new Color32(x.a, x.a, x.a, x.a)).ToArray();
                bri.m_mesh.uv = uvs.ToArray();
                bri.m_mesh.triangles = triangles.ToArray();
                bri.m_fontBaseLimits = new RangeVector { min = 0, max = m_referenceAtlas[id].texture.height };
            }
            finally
            {
                uirenderData.Release();
            }
            yield return 0;

            bri.m_mesh.RecalculateNormals();
            SolveTangents(bri.m_mesh);
            UpdateMaterial();

            bri.m_generatedMaterial = Material;

            bri.m_sizeMetersUnscaled = bri.m_mesh.bounds.size;
            if (m_textCache.TryGetValue(lineId, out TransportLineCacheItem currentVal) && currentVal != null && currentVal?.m_logoData == null)
            {
                m_textCache[lineId].m_logoData = bri;
            }
            else
            {
                m_textCache.Remove(lineId);
            }
            yield break;
        }

        public static IEnumerator<Texture2D> RenderSpriteLine(DynamicSpriteFont font, UITextureAtlas atlas, string spriteName, Color bgColor, string text, float textScale = 1)
        {

            UITextureAtlas.SpriteInfo spriteInfo = atlas[spriteName];
            if (spriteInfo == null)
            {
                CODebugBase<InternalLogChannel>.Warn(InternalLogChannel.UI, "Missing sprite " + spriteName + " in " + atlas.name);
                yield break;
            }
            else
            {
                int height = spriteInfo.texture.height;
                int width = spriteInfo.texture.width;
                var formTexture = new Texture2D(width, height);
                formTexture.SetPixels(spriteInfo.texture.GetPixels());
                TextureScaler.scale(formTexture, width * 2, height * 2);
                Texture2D texText = font.DrawTextToTexture(text);

                Color[] formTexturePixels = formTexture.GetPixels();
                int borderWidth = 8;
                height *= 2;
                width *= 2;


                int targetWidth = width + borderWidth;
                int targetHeight = height + borderWidth;
                TextureScaler.scale(formTexture, targetWidth, targetHeight);
                Color contrastColor = KlyteMonoUtils.ContrastColor(bgColor);
                Color[] targetColorArray = formTexture.GetPixels().Select(x => new Color(contrastColor.r, contrastColor.g, contrastColor.b, x.a)).ToArray();
                Destroy(formTexture);
                var targetBorder = new RectOffset(spriteInfo.border.left * 2, spriteInfo.border.right * 2, spriteInfo.border.top * 2, spriteInfo.border.bottom * 2);

                float textBoundHeight = Mathf.Min(height * .66f, height * .85f - targetBorder.vertical);
                float textBoundWidth = (width * .9f - targetBorder.horizontal);

                var textAreaSize = new Vector4((1f - (textBoundWidth / width)) * (targetBorder.horizontal == 0 ? 0.5f : 1f * targetBorder.left / targetBorder.horizontal) * width, height * (1f - (textBoundHeight / height)) * (targetBorder.vertical == 0 ? 0.5f : 1f * targetBorder.bottom / targetBorder.vertical), textBoundWidth, textBoundHeight);


                float scaleTextTex = Mathf.Min(textAreaSize.z / texText.width, textAreaSize.w / texText.height);
                float proportionTexText = texText.width / texText.height;
                float proportionTextBound = textBoundWidth / textBoundHeight;
                float widthReducer = proportionTextBound / proportionTexText;
                TextureScaler.scale(texText, Mathf.FloorToInt(texText.width * Mathf.Min(widthReducer, 1) * scaleTextTex), Mathf.FloorToInt(texText.height * scaleTextTex));

                Color[] textColors = texText.GetPixels();
                int textWidth = texText.width;
                int textHeight = texText.height;
                Destroy(texText);


                Task<Tuple<Color[], int, int>> task = ThreadHelper.taskDistributor.Dispatch(() =>
                    {
                        TextureRenderUtils.MergeColorArrays(targetColorArray, targetWidth, formTexturePixels.Select(x => new Color(bgColor.r, bgColor.g, bgColor.b, x.a)).ToArray(), borderWidth / 2, borderWidth / 2, width, height);
                        Color[] textOutlineArray = textColors.Select(x => new Color(bgColor.r, bgColor.g, bgColor.b, x.a)).ToArray();
                        int topMerge = Mathf.RoundToInt((textAreaSize.y + ((textBoundHeight - textHeight) / 2)));
                        int leftMerge = Mathf.RoundToInt((textAreaSize.x + ((textBoundWidth - textWidth) / 2)));

                        for (int i = 0; i <= borderWidth / 2; i++)
                        {
                            for (int j = 0; j <= borderWidth / 2; j++)
                            {
                                TextureRenderUtils.MergeColorArrays(targetColorArray, targetWidth, textOutlineArray, leftMerge + i + borderWidth / 4, topMerge + j + borderWidth / 4, textWidth, textHeight);
                            }
                        }
                        TextureRenderUtils.MergeColorArrays(targetColorArray, targetWidth, textColors.Select(x => new Color(contrastColor.r, contrastColor.g, contrastColor.b, x.a)).ToArray(), leftMerge + borderWidth / 2, topMerge + borderWidth / 2, textWidth, textHeight);
                        return Tuple.New(targetColorArray, targetWidth, targetHeight);
                    });
                while (!task.hasEnded)
                {
                    yield return null;
                }

                var targetTexture = new Texture2D(task.result.Second, task.result.Third, TextureFormat.RGBA32, false);
                targetTexture.SetPixels(task.result.First);
                targetTexture.Apply();
                yield return targetTexture;
            }
        }



        internal static readonly int[] kTriangleIndices = new int[]    {
            0,
            1,
            3,
            3,
            1,
            2
        };
        private Vector3[] AlignVertices(PoolList<Vector3> points)
        {
            if (points.Count == 0)
            {
                return points.ToArray();
            }

            var max = new Vector3(points.Select(x => x.x).Max() / 2, points.Select(x => x.y).Max(), points.Select(x => x.z).Max());
            var min = new Vector3(points.Select(x => x.x).Min() / 2, points.Select(x => x.y).Min(), points.Select(x => x.z).Min());
            Vector3 offset = (max + min);

            return points.Select(p => p - offset).ToArray();
        }
        public static void SolveTangents(Mesh mesh)
        {
            int triangleCount = mesh.triangles.Length;
            int vertexCount = mesh.vertices.Length;

            var tan1 = new Vector3[vertexCount];
            var tan2 = new Vector3[vertexCount];
            var tangents = new Vector4[vertexCount];
            for (long a = 0; a < triangleCount; a += 3)
            {
                long i1 = mesh.triangles[a + 0];
                long i2 = mesh.triangles[a + 1];
                long i3 = mesh.triangles[a + 2];
                Vector3 v1 = mesh.vertices[i1];
                Vector3 v2 = mesh.vertices[i2];
                Vector3 v3 = mesh.vertices[i3];
                Vector2 w1 = mesh.uv[i1];
                Vector2 w2 = mesh.uv[i2];
                Vector2 w3 = mesh.uv[i3];
                float x1 = v2.x - v1.x;
                float x2 = v3.x - v1.x;
                float y1 = v2.y - v1.y;
                float y2 = v3.y - v1.y;
                float z1 = v2.z - v1.z;
                float z2 = v3.z - v1.z;
                float s1 = w2.x - w1.x;
                float s2 = w3.x - w1.x;
                float t1 = w2.y - w1.y;
                float t2 = w3.y - w1.y;
                float r = 1.0f / (s1 * t2 - s2 * t1);
                var sdir = new Vector3((t2 * x1 - t1 * x2) * r, (t2 * y1 - t1 * y2) * r, (t2 * z1 - t1 * z2) * r);
                var tdir = new Vector3((s1 * x2 - s2 * x1) * r, (s1 * y2 - s2 * y1) * r, (s1 * z2 - s2 * z1) * r);
                tan1[i1] += sdir;
                tan1[i2] += sdir;
                tan1[i3] += sdir;
                tan2[i1] += tdir;
                tan2[i2] += tdir;
                tan2[i3] += tdir;
            }
            for (long a = 0; a < vertexCount; ++a)
            {
                Vector3 n = mesh.normals[a];
                Vector3 t = tan1[a];
                Vector3 tmp = (t - n * Vector3.Dot(n, t)).normalized;
                tangents[a] = new Vector4(tmp.x, tmp.y, tmp.z)
                {
                    w = (Vector3.Dot(Vector3.Cross(n, t), tan2[a]) < 0.0f) ? -1.0f : 1.0f
                };
            }
            mesh.tangents = tangents;
        }
        public class TransportLineCacheItem
        {
            public BasicRenderInformation m_logoData;
            public string m_startingDestinationName;
            public string m_returningDestinationName;
            public ushort m_halftripStopId;
        }
    }



}