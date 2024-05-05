using System;
using BepInEx;
using UnityEngine;
using EffExt;
using System.Collections.Generic;
using RegionKit.Modules.ShelterBehaviors;
using HUD;
using System.Collections;
using System.Drawing.Drawing2D;
using static Pom.Pom;
using RWCustom;
using RegionKit.Extras;
using RegionKit;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using AssetBundles;
using BepInEx.Logging;
using System.Diagnostics.Eventing.Reader;
using TurnCore;
using Mono.Cecil;
using System.Reflection;
using static Room.Tile;
using System.Linq.Expressions;
using static RegionKit.Modules.Iggy.MessageSystem;

namespace TurnRain
{
    [BepInPlugin(MOD_ID, "TurnRain", "0.1.0")]
    class Plugin : BaseUnityPlugin
    {
        private const string MOD_ID = "ludocrypt.turnrain";

        static bool loaded = false;
        public static AssetBundle trShadersBundle;
        private static Shader trShader;
        private static Material trDeathRainMat;
        public static RoomSettings.RoomEffect.Type RAIN_ANGLE_TYPE = new RoomSettings.RoomEffect.Type("CustomRainAngle", true);
        public static readonly int ShadPropRainDirection = Shader.PropertyToID("_rainDirection");
        private static MethodInfo getEffectDataInfo = typeof(Eff).GetMethod("TryGetExtraData", BindingFlags.NonPublic | BindingFlags.Static);

        // Add hooks
        public void OnEnable()
        {
            Logfix.__SwitchToBepinexLogger(Logger);

            new EffectDefinitionBuilder("CustomRainAngle")
                .SetUADFactory((room, data, firstTimeRealized) => new CustomRainAngle(data))
                .SetCategory("TurnRain")
                .AddFloatField("Angle", 0f, 360f, 1f, 0f)
                .AddFloatField("Flux", 0f, 360f, 1f, 1f)
                .AddFloatField("Speed", 0f, 1f, 0.01f, 0.01f)
                .AddFloatField("Chaos", 0f, 1f, 0.001f, 0.025f)
                .Register();

            On.RoomRain.ctor += RoomRain_ctor;
            On.RoomRain.InitiateSprites += RoomRain_InitiateSprites;
            On.RoomRain.DrawSprites += RoomRain_DrawSprites;
            On.RainWorld.OnModsInit += RainWorld_OnModsInit;
        }


        private void RainWorld_OnModsInit(On.RainWorld.orig_OnModsInit orig, RainWorld self)
        {
            orig.Invoke(self);
            if (!loaded)
            {
                loaded = true;
                try
                {
                    trShadersBundle = AssetBundle.LoadFromFile(AssetManager.ResolveFilePath("assets/cupid/trshaders"));
                    trShader = trShadersBundle.LoadAsset<Shader>("Assets/shaders 1.9.03/TRDeathRain.shader");
                    self.Shaders["TRDeathRain"] = FShader.CreateShader("TRDeathRain", trShader);
                    trDeathRainMat = new Material(trShader);
                }
                catch (Exception e)
                {
                    TurnCore.Logfix.LogInfo(e);
                }
            }
        }

        private void RoomRain_DrawSprites(On.RoomRain.orig_DrawSprites orig, RoomRain self, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
        {
            orig.Invoke(self, sLeaser, rCam, timeStacker, camPos);
            if (self.room.roomSettings.GetEffect(RAIN_ANGLE_TYPE) != null)
            {
                if (sLeaser.sprites[0] != null)
                {
                    sLeaser.sprites[0].shader = ((self.intensity > 0f) ? self.room.game.rainWorld.Shaders["TRDeathRain"] : self.room.game.rainWorld.Shaders["Basic"]);

                    CustomRainAngle rainAngle = self.room.FindUpdatableAndDeletable<CustomRainAngle>();

                    if (rainAngle != null)
                    {
                        Shader.SetGlobalFloat(ShadPropRainDirection, Mathf.Lerp(rainAngle.rainDirectionLast, rainAngle.rainDirection, timeStacker));
                    }
                }
            }
        }

        private void RoomRain_InitiateSprites(On.RoomRain.orig_InitiateSprites orig, RoomRain self, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam)
        {
            orig.Invoke(self, sLeaser, rCam);
            if (self.room.roomSettings.GetEffect(RAIN_ANGLE_TYPE) != null)
            {
                sLeaser.sprites[0].shader = self.room.game.rainWorld.Shaders["TRDeathRain"];
            }
        }

        private void RoomRain_ctor(On.RoomRain.orig_ctor orig, RoomRain self, GlobalRain globalRain, Room rm)
        {
            orig.Invoke(self, globalRain, rm);
            if (rm.roomSettings.GetEffect(RAIN_ANGLE_TYPE) != null)
            {
                if (Eff.TryGetEffectDefinition(RAIN_ANGLE_TYPE, out EffectDefinition effectDefinition))
                {
                    var parameters = new object[] { rm.roomSettings.GetEffect(RAIN_ANGLE_TYPE), null };

                    if (!(bool)getEffectDataInfo.Invoke(null, parameters))
                    {
                        return;
                    }

                    EffectExtraData data = parameters[1] as EffectExtraData;

                    float angle = data.GetFloat("Angle");
                    float flux = (int) data.GetFloat("Flux");

                    self.shelterTex = new Texture2D(rm.TileWidth, rm.TileHeight);

                    for (int i = 0; i < rm.TileWidth; i++)
                    {
                        for (int j = 0; j < rm.TileHeight; j++)
                        {
                            bool solid = rm.GetTile(i, j).Solid;
                            Color color = !solid ? new Color(1f, 0f, 0f) : new Color(0f, 0f, 0f);
                            self.shelterTex.SetPixel(i, j, color);
                            if (!rm.GetTile(i, j).Solid)
                            {
                                bool hit = false;

                                for (float f = -flux; f <= flux; f++)
                                {
                                    double rad = ((double)(angle - f) * (double)Math.PI) / 180.0;

                                    Vector2 ray = new Vector2(i + 0.5f, j + 0.5f);
                                    for (int t = 0; t < rm.TileWidth + rm.TileHeight; t+= 2)
                                    {
                                        ray.x -= (float)Math.Sin(rad) * 2;
                                        ray.y += (float)Math.Cos(rad) * 2;

                                        IntVector2[] points = new IntVector2[]
                                        {
                                        new IntVector2((int)Math.Floor(ray.x), (int)Math.Floor(ray.y)),
                                        new IntVector2((int)Math.Floor(ray.x) + 1, (int)Math.Floor(ray.y)),
                                        new IntVector2((int)Math.Floor(ray.x), (int)Math.Floor(ray.y) + 1),
                                        new IntVector2((int)Math.Floor(ray.x) + 1, (int)Math.Floor(ray.y) + 1),
                                        new IntVector2((int)Math.Floor(ray.x) - 1, (int)Math.Floor(ray.y) + 1),
                                        new IntVector2((int)Math.Floor(ray.x) + 1, (int)Math.Floor(ray.y) - 1),
                                        new IntVector2((int)Math.Floor(ray.x) - 1, (int)Math.Floor(ray.y) - 1),
                                        new IntVector2((int)Math.Floor(ray.x) - 1, (int)Math.Floor(ray.y)),
                                        new IntVector2((int)Math.Floor(ray.x), (int)Math.Floor(ray.y) - 1)
                                        };


                                        foreach (IntVector2 point in points)
                                        {
                                            if (rm.IsPositionInsideBoundries(point))
                                            {
                                                if (rm.GetTile(point).Solid)
                                                {
                                                    hit = true;
                                                    break;
                                                }
                                            }
                                        }

                                        if (hit)
                                        {
                                            break;
                                        }

                                        bool allGood = true;

                                        foreach (IntVector2 point in points)
                                        {
                                            if (rm.IsPositionInsideBoundries(point))
                                            {
                                                if (self.shelterTex.GetPixel(point.x, point.y).g < 0.5)
                                                {
                                                    allGood = false;
                                                    break;
                                                }
                                            }
                                        }

                                        if (allGood)
                                        {
                                            continue;
                                        }

                                    }

                                    if (hit)
                                    {
                                        break;
                                    }
                                }

                                if (!hit)
                                {
                                    self.shelterTex.SetPixel(i, j, new Color(1f, 1f, 0f));
                                }
                            }
                        }
                    }

                    self.shelterTex.wrapMode = TextureWrapMode.Clamp;
                    Futile.atlasManager.UnloadAtlas("RainMask_" + rm.abstractRoom.name);
                    HeavyTexturesCache.LoadAndCacheAtlasFromTexture("RainMask_" + rm.abstractRoom.name, self.shelterTex, false);
                    self.shelterTex.Apply();
                }
            }
        }
    }


    internal class CustomRainAngle : UpdatableAndDeletable
    {
        public EffExt.EffectExtraData data;

        public float rainDirection = 0.0f;
        public float rainDirectionLast = 0.0f;
        public float rainDirectionGetTo = 0.0f;

        public CustomRainAngle(EffExt.EffectExtraData data)
        {
            if (data.Amount == 0) Destroy();
            this.data = data;
        }

        public override void Update(bool eu)
        {
            base.Update(eu);

            if (UnityEngine.Random.value < data.GetFloat("Chaos"))
            {
                this.rainDirectionGetTo = Mathf.Lerp(data.GetFloat("Angle") - data.GetFloat("Flux"), data.GetFloat("Angle") + data.GetFloat("Flux"), UnityEngine.Random.value);
            }
            this.rainDirectionLast = this.rainDirection;
            this.rainDirection = Mathf.Lerp(this.rainDirection, this.rainDirectionGetTo, data.GetFloat("Speed"));
            if (this.rainDirection < this.rainDirectionGetTo)
            {
                this.rainDirection = Mathf.Min(this.rainDirection + 0.0125f, this.rainDirectionGetTo);
            }
            else if (this.rainDirection > this.rainDirectionGetTo)
            {
                this.rainDirection = Mathf.Max(this.rainDirection - 0.0125f, this.rainDirectionGetTo);
            }
        }
    }

}