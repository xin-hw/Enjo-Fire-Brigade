using System.Collections.Generic;
using UnityEngine;

namespace FireRescue
{
    public sealed class ReviewMissionHud : MonoBehaviour
    {
        [SerializeField] FirstReviewMission mission;
        [SerializeField] LocalPlayerInput input;
        [SerializeField] PlayerCarryInteraction carry;
        [SerializeField] HoseConnection hose;
        [SerializeField] FluidSource[] supplySlots = System.Array.Empty<FluidSource>();
        Font font;GUIStyle timer,text,hint,result;
        GUIStyle objectiveStyle, dialogueStyle, bookTaskStyle;
        Camera dialogueCamera;
        (Texture2D Silhouette, Texture2D Interior, Vector2 FillRange)[] supplyIcons;
        public const string RestartHint="按 R 即可重來";
        public bool IsRestartHintVisible=>mission&&mission.CurrentStatus==FirstReviewMission.Status.Failed;
        internal IReadOnlyList<FluidSource> SupplySlots => supplySlots;
        internal FluidSource FocusedSupply
        {
            get
            {
                if (!mission || !mission.AllowsInput) return null;
                FluidSource connected = hose ? hose.Source : null;
                FluidSource candidate = connected ? connected : input ? input.NearbySource : null;
                return candidate && candidate.HasCapacity ? candidate : null;
            }
        }

        public void Initialize(FirstReviewMission state,LocalPlayerInput controls,PlayerCarryInteraction items,HoseConnection connection)
        {mission=state;input=controls;carry=items;hose=connection;}
        void Awake(){font=Font.CreateDynamicFontFromOSFont("Microsoft JhengHei",22);}
        void OnDestroy()
        {
            if(font)Destroy(font);
            if (supplyIcons == null) return;
            foreach (var icon in supplyIcons)
            {
                Destroy(icon.Silhouette);
                Destroy(icon.Interior);
            }
        }
        void OnGUI()
        {
            if(!mission)return;
            if(timer==null)
            {
                text=new GUIStyle(GUI.skin.label){font=font,fontSize=20,normal={textColor=Color.white}};
                timer=new GUIStyle(text){fontSize=36,alignment=TextAnchor.MiddleCenter,fontStyle=FontStyle.Bold};
                result=new GUIStyle(timer){fontSize=24};
                hint=new GUIStyle(timer){fontSize=16,fontStyle=FontStyle.Normal,normal={textColor=new Color(.85f,.88f,.91f)}};
                objectiveStyle = new GUIStyle(hint) { fontSize = 18 };
                dialogueStyle = new GUIStyle(objectiveStyle) { wordWrap = true, normal = { textColor = Color.white } };
                bookTaskStyle = new GUIStyle(dialogueStyle) { alignment = TextAnchor.MiddleLeft, wordWrap = false };
            }
            var old=GUI.matrix;GUI.matrix=Matrix4x4.Scale(new Vector3(Screen.width/1280f,Screen.height/720f,1));
            int seconds=Mathf.CeilToInt(mission.Remaining);
            Box(new Rect(550,12,180,64));GUI.Label(new Rect(550,12,180,60),$"{seconds/60:00}:{seconds%60:00}",timer);
            DrawSupplies();
            var sequence = mission.BookRescue;
            if (sequence && mission.AllowsInput) DrawBookObjective(sequence);
            if(mission.CurrentStatus==FirstReviewMission.Status.Won)
            {
                Box(new Rect(360,260,560,100));GUI.Label(new Rect(380,275,520,65),"第一關完成！",timer);
            }
            else if(IsRestartHintVisible)
            {
                // Small text only: no clickable button or large restart dialog.
                ShadowLabel(new Rect(500,305,280,36),"失敗",result);
                ShadowLabel(new Rect(500,342,280,26),RestartHint,hint);
            }
            if (sequence) DrawBookDialogue(sequence);
            GUI.matrix=old;
        }

        void DrawBookObjective(BookRescueSequence sequence)
        {
            string objective = carry && carry.IsCarrying ? "按 E，" + BookRescueSequence.BookRequest : sequence.CurrentObjective;
            var objectiveBox = new Rect(375,82,530,32);
            Paint(objectiveBox, new Color(.10f,.13f,.16f,.90f), 6f);
            GUI.Label(objectiveBox, objective, objectiveStyle);
        }

        void DrawBookDialogue(BookRescueSequence sequence)
        {
            bool fulfilled = mission.Request && mission.Request.IsFulfilled;
            bool showJoke = !fulfilled && mission.AllowsInput && input && sequence.CanUseSoda &&
                (input.transform.position - sequence.Man.position).sqrMagnitude <= 9f;
            if (!dialogueCamera) dialogueCamera = Camera.main;
            if (!dialogueCamera) return;
            Vector3 point = dialogueCamera.WorldToViewportPoint(sequence.DialogueAnchor);
            if (point.z <= 0f || point.x < 0f || point.x > 1f || point.y < 0f || point.y > 1f) return;

            float center = Mathf.Clamp(point.x * 1280f, 99f, 1181f);
            float bottom = Mathf.Clamp((1f - point.y) * 720f - 31f, 178f, 710f);
            if (showJoke)
                DrawDialogueText(new Rect(Mathf.Clamp(center - 226f, 10f, 818f), bottom - 58f, 452f, 30f),
                    BookRescueSequence.Joke, dialogueStyle);
            var box = new Rect(center - 84f, bottom - 24f, 16f, 16f);
            Color mark = fulfilled ? new Color(.10f,.48f,.24f) : new Color(.18f,.20f,.23f);
            GUI.DrawTexture(new Rect(box.x - 1f, box.y - 1f, box.width + 2f, box.height + 2f),
                Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, new Color(1f,.98f,.92f), 3f, 4f);
            GUI.DrawTexture(box, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, mark, 1.5f, 3f);
            if (fulfilled)
            {
                Vector2 joint = new(box.x + 6.5f, box.y + 11.5f);
                DrawStroke(new Vector2(box.x + 3.5f, box.y + 8f), joint, mark);
                DrawStroke(joint, new Vector2(box.x + 12.5f, box.y + 4.5f), mark);
            }
            DrawDialogueText(new Rect(box.xMax + 8f, bottom - 30f, 156f, 29f), BookRescueSequence.BookRequest, bookTaskStyle);
        }

        static void DrawDialogueText(Rect label, string line, GUIStyle style)
        {
            Color previous = GUI.color;
            GUI.color = new Color(1f,.98f,.93f);
            GUI.Label(new Rect(label.x - 1f, label.y, label.width, label.height), line, style);
            GUI.Label(new Rect(label.x + 1f, label.y, label.width, label.height), line, style);
            GUI.Label(new Rect(label.x, label.y - 1f, label.width, label.height), line, style);
            GUI.Label(new Rect(label.x, label.y + 1f, label.width, label.height), line, style);
            GUI.color = new Color(.14f,.17f,.20f);
            GUI.Label(label, line, style);
            GUI.color = previous;
        }

        static void DrawStroke(Vector2 start, Vector2 end, Color color)
        {
            var matrix = GUI.matrix;
            Vector2 delta = end - start;
            var rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            GUI.matrix = matrix * Matrix4x4.TRS(start, rotation, Vector3.one);
            Paint(new Rect(-.75f, -1f, delta.magnitude + 1.5f, 2f), color, 1f);
            GUI.matrix = matrix;
        }

        void DrawSupplies()
        {
            if (Event.current.type != EventType.Repaint) return;
            var focused = FocusedSupply;
            int count = supplySlots.Length;
            if (count == 0 && !focused) return;
            supplyIcons ??= new[] { MakeSupplyIcon(ElementKind.Water), MakeSupplyIcon(ElementKind.DryPowder),
                MakeSupplyIcon(ElementKind.CarbonDioxide) };
            const float slotWidth = 60f;
            float width = Mathf.Max(count, 1) * slotWidth + 12f;
            var panel = new Rect(1256f - width, 16f, width, 62f);
            Paint(panel, new Color(.08f,.11f,.15f,.94f), 8f);
            if (count == 0)
            {
                DrawSupply(focused, true, panel.x + 6f, panel.y);
                return;
            }
            for (int i = 0; i < count; i++)
            {
                var source = supplySlots[i];
                if (source) DrawSupply(source, source == focused, panel.x + 6f + i * slotWidth, panel.y);
            }
        }

        void DrawSupply(FluidSource source, bool focused, float x, float y)
        {
            int index = source.Element switch { ElementKind.Water => 0, ElementKind.CarbonDioxide => 2, _ => 1 };
            Color accent = index switch
            {
                0 => new Color(.35f,.76f,.96f),
                2 => new Color(.50f,.88f,.73f),
                _ => new Color(.97f,.81f,.55f)
            };
            bool ready = source.isActiveAndEnabled;
            var icon = supplyIcons[index];
            var glyph = new Rect(x + 7f, y + 3f, 46f, 51f);
            if (focused)
                Paint(new Rect(x + 17f, y + 54f, 26f, 2f), accent, 1f);
            Color rim = ready ? Color.Lerp(accent, new Color(.78f,.82f,.86f), .43f) : new Color(.43f,.49f,.55f);
            GUI.DrawTexture(glyph, icon.Silhouette, ScaleMode.StretchToFill, true, 0f, rim, 0f, 0f);
            GUI.DrawTexture(glyph, icon.Interior, ScaleMode.StretchToFill, true, 0f, new Color(.17f,.21f,.26f), 0f, 0f);
            if (ready)
                DrawSupplyFill(glyph, icon.Interior, icon.FillRange, accent, Mathf.Clamp01(source.RemainingFraction));
            else
                DrawSupplyLock(x + 39f, y + 37f);
            if (source.IsRefilling)
            {
                var tip = new Vector2(x + 49f, y + 14f);
                DrawStroke(tip + new Vector2(-4f, 4f), tip, accent);
                DrawStroke(tip, tip + new Vector2(4f, 4f), accent);
            }
        }

        static void DrawSupplyFill(Rect glyph, Texture2D mask, Vector2 range, Color accent, float fraction)
        {
            if (fraction <= 0f) return;
            float height = Mathf.Max(1f / glyph.height, (range.y - range.x) * fraction);
            var filled = new Rect(glyph.x, glyph.yMax - glyph.height * (range.x + height), glyph.width, glyph.height * height);
            Color previous = GUI.color;
            GUI.color = accent;
            GUI.DrawTextureWithTexCoords(filled, mask, new Rect(0f, range.x, 1f, height));
            GUI.color = previous;
        }

        static void DrawSupplyLock(float x, float y)
        {
            var ink = new Color(.81f,.85f,.89f);
            GUI.DrawTexture(new Rect(x + 2f, y - 5f, 8f, 10f), Texture2D.whiteTexture,
                ScaleMode.StretchToFill, true, 0f, ink, 1.5f, 4f);
            Paint(new Rect(x, y, 12f, 9f), ink, 2f);
        }

        static void Paint(Rect rect, Color colour, float radius) =>
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, colour, 0f, radius);

        static (Texture2D Silhouette, Texture2D Interior, Vector2 FillRange) MakeSupplyIcon(ElementKind element)
        {
            const int size = 48;
            var rimPixels = new Color32[size * size];
            var insidePixels = new Color32[size * size];
            var fillRange = new Vector2(1f, 0f);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + .5f) / size * 2f - 1f;
                float v = (y + .5f) / size * 2f - 1f;
                float distance;
                if (element == ElementKind.Water)
                {
                    float bulb = new Vector2(u, v + .23f).magnitude - .53f;
                    float taper = Mathf.Max(Mathf.Abs(u) - (.77f - v) * .53f, Mathf.Max(v - .77f, -.27f - v));
                    distance = Mathf.Min(bulb, taper);
                }
                else if (element == ElementKind.CarbonDioxide)
                {
                    float body = RoundedIconBox(u, v + .24f, .39f, .51f, .13f);
                    float shoulders = RoundedIconBox(u, v - .22f, .30f, .24f, .12f);
                    float neck = RoundedIconBox(u, v - .46f, .18f, .25f, .06f);
                    float cap = RoundedIconBox(u, v - .65f, .23f, .12f, .05f);
                    distance = Mathf.Min(Mathf.Min(body, shoulders), Mathf.Min(neck, cap));
                }
                else
                {
                    float halfWidth = Mathf.Lerp(.55f, .43f, Mathf.InverseLerp(-.73f, .55f, v));
                    float bag = RoundedIconBox(u, v + .10f, halfWidth, .64f, .10f);
                    float seal = RoundedIconBox(u, v - .58f, .46f, .15f, .06f);
                    distance = Mathf.Min(bag, seal);
                }
                int pixel = y * size + x;
                rimPixels[pixel] = Coverage(distance);
                insidePixels[pixel] = Coverage(distance + .065f);
                if (insidePixels[pixel].a >= 128)
                {
                    fillRange.x = Mathf.Min(fillRange.x, y / (float)size);
                    fillRange.y = Mathf.Max(fillRange.y, (y + 1f) / size);
                }
            }

            return (MakeMask(rimPixels), MakeMask(insidePixels), fillRange);

            static Color32 Coverage(float distance) =>
                new(255, 255, 255, (byte)Mathf.RoundToInt(255f * Mathf.InverseLerp(.028f, -.028f, distance)));

            static Texture2D MakeMask(Color32[] pixels)
            {
                var mask = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                mask.SetPixels32(pixels);
                mask.Apply(false, true);
                return mask;
            }
        }

        static float RoundedIconBox(float x, float y, float halfWidth, float halfHeight, float radius)
        {
            var edge = new Vector2(Mathf.Abs(x) - halfWidth + radius, Mathf.Abs(y) - halfHeight + radius);
            return Vector2.Max(edge, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(edge.x, edge.y), 0f) - radius;
        }
        static void ShadowLabel(Rect rect,string value,GUIStyle style)
        {
            var color=GUI.color;GUI.color=new Color(0,0,0,.85f);
            GUI.Label(new Rect(rect.x+1,rect.y+1,rect.width,rect.height),value,style);
            GUI.color=color;GUI.Label(rect,value,style);
        }
        static void Box(Rect r){Color old=GUI.color;GUI.color=new Color(.07f,.10f,.14f,.88f);GUI.DrawTexture(r,Texture2D.whiteTexture);GUI.color=old;}
    }
}
