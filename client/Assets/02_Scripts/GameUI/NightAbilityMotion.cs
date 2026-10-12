using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WhoisntCitizen.Game;

namespace WhoisntCitizen.GameUI
{
    /// <summary>
    /// The role motion played on my character in the night room (<see cref="NightRoomView"/>) when the server accepts my
    /// night action. Each ability has its own move and effect: the raider lunges and slashes, the parrot hops and its
    /// spyglass flashes, the captain's compass turns, the doctor raises a shield of light, the lookout sweeps a spyglass
    /// beam, the boatswain pulls a rope tight and seals it, the drunk sways among spirit wisps.
    /// It is only drawn on my screen and shows nothing the server did not tell me (the result still comes at dawn).
    /// The avatar is driven in its own local space (rest: position 0, scale 1, no rotation) and put back at the end.
    /// Effect positions are stage coordinates (the layers sit at the stage centre).
    /// </summary>
    internal sealed class NightAbilityMotion
    {
        private readonly RectTransform back;   // under the figures (glows, floor compass)
        private readonly RectTransform front;  // over the figures (slashes, shields, sparks)
        private readonly RectTransform shake;  // the stage content, shaken by hits
        private readonly List<GameObject> spawned = new List<GameObject>();
        private RectTransform avatar;

        public NightAbilityMotion(RectTransform backLayer, RectTransform frontLayer, RectTransform shakeRoot)
        {
            back = backLayer;
            front = frontLayer;
            shake = shakeRoot;
        }

        /// <summary>The colour of an ability (cut-in flash and caption).</summary>
        public static Color TintFor(string actionCode)
        {
            switch (actionCode)
            {
                case ActionCodes.SelectAttackTarget: return new Color(1f, 0.25f, 0.2f);
                case ActionCodes.WatchAction: return new Color(1f, 0.62f, 0.25f);
                case ActionCodes.InvestigateFaction: return new Color(1f, 0.8f, 0.35f);
                case ActionCodes.Protect: return new Color(1f, 0.93f, 0.62f);
                case ActionCodes.WatchVisitors: return new Color(1f, 0.95f, 0.72f);
                case ActionCodes.Block: return new Color(0.62f, 0.86f, 1f);
                case ActionCodes.ReadCorpseRole: return new Color(0.45f, 0.75f, 1f);
                default: return new Color(0.85f, 0.88f, 1f);
            }
        }

        /// <summary>Plays the move of this ability on the avatar (pivot at its feet) standing at feet, this tall.</summary>
        public IEnumerator Play(string actionCode, RectTransform figure, Vector2 feet, float height)
        {
            Reset();
            avatar = figure;
            switch (actionCode)
            {
                case ActionCodes.SelectAttackTarget: yield return Attack(feet, height); break;
                case ActionCodes.WatchAction: yield return ParrotWatch(feet, height); break;
                case ActionCodes.InvestigateFaction: yield return Compass(feet, height); break;
                case ActionCodes.Protect: yield return Shield(feet, height); break;
                case ActionCodes.WatchVisitors: yield return Spyglass(feet, height); break;
                case ActionCodes.Block: yield return RopeSeal(feet, height); break;
                case ActionCodes.ReadCorpseRole: yield return SpiritWisps(feet, height); break;
                default: yield return Glow(feet, height, TintFor(actionCode)); break;
            }
            Reset();
        }

        /// <summary>Removes the effects and puts the avatar and the stage back at rest.</summary>
        public void Reset()
        {
            foreach (GameObject go in spawned)
            {
                if (go != null) UnityEngine.Object.Destroy(go);
            }
            spawned.Clear();
            if (avatar != null) Pose(Vector2.zero, Vector2.one, 0f);
            avatar = null;
            if (shake != null) shake.anchoredPosition = Vector2.zero;
        }

        // ------------------------------------------------------------------ moves

        // Raider: wind up, lunge to the right with two crossing slashes, a red flash and a jolt.
        private IEnumerator Attack(Vector2 feet, float h)
        {
            Vector2 hit = feet + new Vector2(h * 0.4f, h * 0.52f);
            Color red = new Color(1f, 0.22f, 0.18f);
            Color blade = new Color(1f, 0.92f, 0.86f);
            Image glow = Spawn(back, NightFxSprites.Dot, hit, Vector2.one * h * 2.2f, Fade(red, 0f));
            Image cut1 = Spawn(front, NightFxSprites.Streak, hit, new Vector2(h * 1.25f, h * 0.13f), Fade(blade, 0f), 32f);
            Image cut2 = Spawn(front, NightFxSprites.Streak, hit, new Vector2(h * 1.25f, h * 0.13f), Fade(blade, 0f), -28f);
            Image rim1 = Spawn(front, NightFxSprites.Streak, hit, new Vector2(h * 1.4f, h * 0.24f), Fade(red, 0f), 32f);
            Image rim2 = Spawn(front, NightFxSprites.Streak, hit, new Vector2(h * 1.4f, h * 0.24f), Fade(red, 0f), -28f);
            rim1.transform.SetSiblingIndex(cut1.transform.GetSiblingIndex());
            rim2.transform.SetSiblingIndex(cut2.transform.GetSiblingIndex());
            yield return Run(1.0f, t =>
            {
                float wind = Seg(t, 0f, 0.14f), lunge = EaseOut(Seg(t, 0.14f, 0.26f)), recover = EaseInOut(Seg(t, 0.55f, 0.9f));
                float x = Mathf.Lerp(-14f * wind, 70f, lunge) * (1f - recover);
                float angle = Mathf.Lerp(6f * wind, -10f, lunge) * (1f - recover);
                float squash = 0.07f * wind * (1f - lunge) - 0.05f * lunge * (1f - recover);
                Pose(new Vector2(x, 0f), new Vector2(1f + squash, 1f - squash), angle);

                float fade = 1f - Seg(t, 0.45f, 0.8f);
                Slash(cut1, rim1, EaseOut(Seg(t, 0.17f, 0.27f)), fade);
                Slash(cut2, rim2, EaseOut(Seg(t, 0.25f, 0.35f)), fade);
                glow.color = Fade(red, 0.5f * Bell(Seg(t, 0.18f, 0.7f)));
                Shake(t, 0.2f, 0.5f, 14f);
            });
        }

        private static void Slash(Image blade, Image rim, float drawn, float fade)
        {
            Vector3 scale = new Vector3(drawn, 1f, 1f);
            blade.rectTransform.localScale = scale;
            rim.rectTransform.localScale = scale;
            float alpha = drawn > 0f ? fade : 0f;
            blade.color = Fade(blade.color, alpha);
            rim.color = Fade(rim.color, 0.7f * alpha);
        }

        // Parrot: two hops, a flash from the spyglass with a turning star, a ring and a burst of feathers.
        private IEnumerator ParrotWatch(Vector2 feet, float h)
        {
            Vector2 eye = feet + new Vector2(h * 0.12f, h * 0.74f);
            Color gold = new Color(1f, 0.86f, 0.42f);
            Image flare = Spawn(front, NightFxSprites.Dot, eye, Vector2.one * h * 0.3f, Fade(gold, 0f));
            Image ray1 = Spawn(front, NightFxSprites.Streak, eye, new Vector2(h * 0.8f, h * 0.05f), Fade(Color.white, 0f));
            Image ray2 = Spawn(front, NightFxSprites.Streak, eye, new Vector2(h * 0.8f, h * 0.05f), Fade(Color.white, 0f), 90f);
            Image pulse = Spawn(front, NightFxSprites.Ring, eye, Vector2.one * 40f, Fade(gold, 0f));
            Color[] plumage = { new Color(0.9f, 0.15f, 0.12f), new Color(0.1f, 0.62f, 0.6f), new Color(1f, 0.8f, 0.2f) };
            var feathers = new Image[7];
            for (int i = 0; i < feathers.Length; i++)
            {
                feathers[i] = Spawn(front, NightFxSprites.Dot, feet, new Vector2(h * 0.06f, h * 0.12f), Fade(plumage[i % 3], 0f));
            }
            Vector2 chest = feet + new Vector2(0f, h * 0.5f);
            yield return Run(1.35f, t =>
            {
                float hop = Seg(t, 0f, 0.5f);
                float lift = hop < 1f ? Mathf.Abs(Mathf.Sin(hop * Mathf.PI * 2f)) * 34f : 0f;
                float squash = hop < 1f ? 0.05f * Mathf.Sin(hop * Mathf.PI * 4f) : 0f;
                Pose(new Vector2(0f, lift), new Vector2(1f - squash * 0.6f, 1f + squash), 0f);

                float glint = Bell(Seg(t, 0.3f, 1.2f));
                flare.rectTransform.sizeDelta = Vector2.one * h * (0.15f + 0.35f * glint);
                flare.color = Fade(gold, glint);
                float turn = 90f * Seg(t, 0.3f, 1.2f);
                ray1.rectTransform.localRotation = Quaternion.Euler(0f, 0f, turn);
                ray2.rectTransform.localRotation = Quaternion.Euler(0f, 0f, turn + 90f);
                ray1.rectTransform.localScale = ray2.rectTransform.localScale = Vector3.one * (0.6f + 0.4f * glint);
                ray1.color = ray2.color = Fade(Color.white, 0.9f * glint);

                float ring = Seg(t, 0.35f, 0.9f);
                pulse.rectTransform.sizeDelta = Vector2.one * (40f + h * 0.75f * EaseOut(ring));
                pulse.color = Fade(gold, ring > 0f ? 0.9f * (1f - ring) : 0f);

                for (int i = 0; i < feathers.Length; i++)
                {
                    float e = t - (0.12f + i * 0.03f);
                    if (e <= 0f) continue;
                    float angle = Mathf.Lerp(25f, 155f, Hash(i, 1)) * Mathf.Deg2Rad;
                    float speed = h * Mathf.Lerp(0.6f, 1.1f, Hash(i, 2));
                    Vector2 p = chest + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed * e + new Vector2(0f, -h * 0.9f * e * e);
                    RectTransform rect = feathers[i].rectTransform;
                    rect.anchoredPosition = p;
                    rect.localRotation = Quaternion.Euler(0f, 0f, (Hash(i, 3) - 0.5f) * 720f * e);
                    feathers[i].color = Fade(feathers[i].color, 1f - Seg(e, 0.55f, 1.05f));
                }
            });
        }

        // Captain: a compass rose turns on the floor, a needle spins above the head and settles.
        private IEnumerator Compass(Vector2 feet, float h)
        {
            Color gold = new Color(1f, 0.8f, 0.35f);
            RectTransform flat = Group(back, feet + new Vector2(0f, h * 0.02f), new Vector2(1f, 0.38f));
            RectTransform spin = Group(flat, Vector2.zero, Vector2.one);
            var marks = new List<Image>
            {
                Spawn(spin, NightFxSprites.Ring, Vector2.zero, Vector2.one * h * 1.9f, gold),
                Spawn(spin, NightFxSprites.Ring, Vector2.zero, Vector2.one * h * 1.3f, Fade(gold, 0.5f))
            };
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f * Mathf.Deg2Rad;
                float size = i % 2 == 0 ? 0.09f : 0.05f;
                marks.Add(Spawn(spin, NightFxSprites.Dot, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * h * 0.83f, Vector2.one * h * size, gold));
            }
            Vector2 top = feet + new Vector2(0f, h * 1.12f);
            Image dial = Spawn(front, NightFxSprites.Ring, top, Vector2.one * h * 0.42f, Fade(gold, 0f));
            Image needle = Spawn(front, NightFxSprites.Streak, top, new Vector2(h * 0.38f, h * 0.06f), Fade(Color.white, 0f));
            Image glow = Spawn(back, NightFxSprites.Dot, feet + new Vector2(0f, h * 0.5f), Vector2.one * h * 1.4f, Fade(gold, 0f));
            yield return Run(1.45f, t =>
            {
                float open = EaseOut(Seg(t, 0f, 0.35f)), close = Seg(t, 1.0f, 1.45f);
                flat.localScale = new Vector3(open, 0.38f * open, 1f);
                spin.localRotation = Quaternion.Euler(0f, 0f, -200f * EaseInOut(Seg(t, 0f, 1.3f)));
                foreach (Image mark in marks) mark.color = Fade(mark.color, 1f - close);

                float spinIn = Seg(t, 0.15f, 0.3f) * (1f - close);
                float n = Seg(t, 0.2f, 1.1f);
                needle.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f + 720f * (1f - Mathf.Pow(1f - n, 3f)));
                needle.color = Fade(Color.white, spinIn);
                dial.color = Fade(gold, 0.9f * spinIn);
                glow.color = Fade(gold, 0.35f * Bell(Seg(t, 0.1f, 1.3f)));
                Pose(new Vector2(0f, 10f * Bell(Seg(t, 0.15f, 1.3f))), Vector2.one, 0f);
            });
        }

        // Doctor: a warm glow, a shield of light opening around the body, sparks rising.
        private IEnumerator Shield(Vector2 feet, float h)
        {
            Vector2 chest = feet + new Vector2(0f, h * 0.5f);
            Color warm = new Color(1f, 0.93f, 0.62f);
            Image glow = Spawn(back, NightFxSprites.Dot, chest, Vector2.one * h * 1.9f, Fade(warm, 0f));
            Image shield = Spawn(front, NightFxSprites.Ring, chest, Vector2.one * h * 0.55f, Fade(warm, 0f));
            Image wave = Spawn(front, NightFxSprites.Ring, chest, Vector2.one * h * 0.8f, Fade(Color.white, 0f));
            var sparks = new Image[10];
            for (int i = 0; i < sparks.Length; i++)
            {
                sparks[i] = Spawn(front, NightFxSprites.Dot, feet, Vector2.one * h * Mathf.Lerp(0.04f, 0.07f, Hash(i, 4)), Fade(Color.white, 0f));
            }
            yield return Run(1.5f, t =>
            {
                float open = EaseOut(Seg(t, 0.05f, 0.45f)), close = Seg(t, 1.05f, 1.5f);
                glow.color = Fade(warm, 0.65f * Seg(t, 0f, 0.3f) * (1f - close));
                shield.rectTransform.sizeDelta = Vector2.one * h * (0.55f + 0.95f * open) * (1f + 0.035f * Mathf.Sin(t * 9f));
                shield.color = Fade(warm, (0.95f - 0.35f * open) * (1f - close) * Seg(t, 0f, 0.08f));
                float w = Seg(t, 0.25f, 0.85f);
                wave.rectTransform.sizeDelta = Vector2.one * h * (0.8f + 0.9f * w);
                wave.color = Fade(Color.white, w > 0f ? 0.6f * (1f - w) : 0f);
                for (int i = 0; i < sparks.Length; i++)
                {
                    float s = Seg(t, 0.15f + i * 0.06f, 1.15f + i * 0.06f);
                    float x = (Hash(i, 5) - 0.5f) * h * 0.8f;
                    sparks[i].rectTransform.anchoredPosition = feet + new Vector2(x + Mathf.Sin(s * 6f + i) * 8f, h * 0.1f + h * 0.8f * s);
                    sparks[i].color = Fade(Color.white, Bell(s));
                }
                float rise = Bell(Seg(t, 0.05f, 1.4f));
                Pose(new Vector2(0f, 8f * rise), Vector2.one * (1f + 0.03f * rise), 0f);
            });
        }

        // Lookout: a spyglass beam sweeps the night from left to right; the body leans with it.
        private IEnumerator Spyglass(Vector2 feet, float h)
        {
            Vector2 eye = feet + new Vector2(h * 0.06f, h * 0.76f);
            Color light = new Color(1f, 0.95f, 0.72f);
            Image beam = Spawn(front, NightFxSprites.Cone, eye, new Vector2(h * 1.7f, h * 0.95f), Fade(light, 0f), 168f);
            Image glint = Spawn(front, NightFxSprites.Dot, eye, Vector2.one * h * 0.12f, Fade(Color.white, 0f));
            yield return Run(1.5f, t =>
            {
                float sweep = EaseInOut(Seg(t, 0.25f, 1.15f));
                float on = Seg(t, 0.1f, 0.3f) * (1f - Seg(t, 1.15f, 1.5f));
                beam.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(168f, 12f, sweep));
                beam.color = Fade(light, 0.38f * on);
                glint.rectTransform.sizeDelta = Vector2.one * h * (0.12f + 0.12f * Bell(Seg(t, 0.1f, 1.4f)));
                glint.color = Fade(Color.white, on);
                Pose(Vector2.zero, Vector2.one, Mathf.Lerp(4f, -4f, sweep) * on);
            });
        }

        // Boatswain: pulls back as a rope loop tightens around the waist, then a seal snaps shut with a jolt.
        private IEnumerator RopeSeal(Vector2 feet, float h)
        {
            Color rope = new Color(0.78f, 0.58f, 0.32f);
            Color rope2 = new Color(0.55f, 0.38f, 0.2f);
            Color seal = new Color(0.65f, 0.88f, 1f);
            RectTransform flat = Group(front, feet + new Vector2(0f, h * 0.38f), new Vector2(1f, 0.42f));
            Image outer = Spawn(flat, NightFxSprites.Ring, Vector2.zero, Vector2.one * h * 3.6f, Fade(rope2, 0f));
            Image inner = Spawn(flat, NightFxSprites.Ring, Vector2.zero, Vector2.one * h * 3.2f, Fade(rope, 0f));
            Vector2 chest = feet + new Vector2(0f, h * 0.55f);
            Image cross1 = Spawn(front, NightFxSprites.Streak, chest, new Vector2(h * 0.62f, h * 0.09f), Fade(seal, 0f), 45f);
            Image cross2 = Spawn(front, NightFxSprites.Streak, chest, new Vector2(h * 0.62f, h * 0.09f), Fade(seal, 0f), -45f);
            Image flash = Spawn(back, NightFxSprites.Dot, chest, Vector2.one * h * 1.5f, Fade(seal, 0f));
            yield return Run(1.35f, t =>
            {
                float pull = EaseInOut(Seg(t, 0.05f, 0.38f)), snap = EaseOut(Seg(t, 0.42f, 0.58f));
                Pose(new Vector2(-26f * pull * (1f - snap), 0f), Vector2.one, 7f * pull * (1f - snap));

                float close = 1f - Seg(t, 0.9f, 1.35f);
                float a = Seg(t, 0f, 0.45f);
                inner.rectTransform.sizeDelta = Vector2.one * h * Mathf.Lerp(3.2f, 1.05f, a * a);
                inner.color = Fade(rope, Seg(t, 0f, 0.1f) * close);
                float b = Seg(t, 0.06f, 0.5f);
                outer.rectTransform.sizeDelta = Vector2.one * h * Mathf.Lerp(3.6f, 1.15f, b * b);
                outer.color = Fade(rope2, 0.7f * Seg(t, 0.06f, 0.16f) * close);

                float p = Seg(t, 0.42f, 0.6f);
                cross1.rectTransform.localScale = cross2.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.6f, 1f, EaseOut(p));
                cross1.color = cross2.color = Fade(seal, p > 0f ? 1f - Seg(t, 0.95f, 1.35f) : 0f);
                flash.color = Fade(seal, 0.45f * Bell(Seg(t, 0.42f, 0.9f)));
                Shake(t, 0.42f, 0.65f, 9f);
            });
        }

        // Drunk: sways as blue spirit wisps spiral up from the chest.
        private IEnumerator SpiritWisps(Vector2 feet, float h)
        {
            Vector2 chest = feet + new Vector2(0f, h * 0.55f);
            Color spirit = new Color(0.42f, 0.72f, 1f);
            Color pale = new Color(0.6f, 1f, 0.95f);
            Image glow = Spawn(back, NightFxSprites.Dot, chest, Vector2.one * h * 1.7f, Fade(spirit, 0f));
            var wisps = new Image[9];
            for (int i = 0; i < wisps.Length; i++)
            {
                wisps[i] = Spawn(front, NightFxSprites.Dot, chest, Vector2.one * h * Mathf.Lerp(0.08f, 0.13f, Hash(i, 6)), Fade(i % 2 == 0 ? spirit : pale, 0f));
            }
            yield return Run(1.6f, t =>
            {
                float sway = Bell(Seg(t, 0f, 1.6f));
                Pose(new Vector2(10f * Mathf.Sin(t * 7.5f) * sway, 0f), Vector2.one, 7f * Mathf.Sin(t * 7.5f + 0.6f) * sway);
                glow.color = Fade(spirit, 0.5f * sway);
                for (int i = 0; i < wisps.Length; i++)
                {
                    float s = Seg(t, 0.1f + i * 0.08f, 1.05f + i * 0.08f);
                    float angle = i * 0.7f + s * 4f;
                    float r = h * (0.18f + 0.22f * s);
                    wisps[i].rectTransform.anchoredPosition = chest + new Vector2(Mathf.Cos(angle) * r, -h * 0.2f + s * h * 0.95f);
                    wisps[i].rectTransform.localScale = Vector3.one * (0.7f + 0.6f * s);
                    wisps[i].color = Fade(wisps[i].color, 0.85f * Bell(s));
                }
            });
        }

        // Any other ability: a glow in its colour and a small lift.
        private IEnumerator Glow(Vector2 feet, float h, Color tint)
        {
            Image glow = Spawn(back, NightFxSprites.Dot, feet + new Vector2(0f, h * 0.5f), Vector2.one * h * 1.8f, Fade(tint, 0f));
            yield return Run(1.0f, t =>
            {
                float k = Bell(Seg(t, 0f, 1f));
                glow.color = Fade(tint, 0.6f * k);
                Pose(new Vector2(0f, 6f * k), Vector2.one, 0f);
            });
        }

        // ------------------------------------------------------------------ helpers

        private static IEnumerator Run(float seconds, Action<float> step)
        {
            float start = Time.unscaledTime;
            for (float t = 0f; t < seconds; t = Time.unscaledTime - start)
            {
                step(t);
                yield return null;
            }
            step(seconds);
        }

        private void Pose(Vector2 offset, Vector2 scale, float angle)
        {
            avatar.anchoredPosition = offset;
            avatar.localScale = new Vector3(scale.x, scale.y, 1f);
            avatar.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        private void Shake(float t, float from, float to, float amplitude)
        {
            float k = t < from ? 0f : 1f - Seg(t, from, to);
            shake.anchoredPosition = new Vector2(Mathf.Sin(t * 95f), 0.5f * Mathf.Cos(t * 77f)) * amplitude * k;
        }

        private Image Spawn(RectTransform layer, Sprite sprite, Vector2 position, Vector2 size, Color color, float angle = 0f)
        {
            var go = new GameObject(sprite.name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.layer = layer.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(layer, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(sprite.pivot.x / sprite.rect.width, sprite.pivot.y / sprite.rect.height);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            rect.localRotation = Quaternion.Euler(0f, 0f, angle);
            Image image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            spawned.Add(go);
            return image;
        }

        private RectTransform Group(RectTransform layer, Vector2 position, Vector2 scale)
        {
            var go = new GameObject("FxGroup", typeof(RectTransform));
            go.layer = layer.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(layer, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = Vector2.zero;
            rect.anchoredPosition = position;
            rect.localScale = new Vector3(scale.x, scale.y, 1f);
            spawned.Add(go);
            return rect;
        }

        private static Color Fade(Color color, float alpha)
        {
            color.a = Mathf.Clamp01(alpha);
            return color;
        }

        private static float Seg(float t, float from, float to) => Mathf.Clamp01((t - from) / (to - from));
        private static float EaseOut(float x) => 1f - (1f - x) * (1f - x) * (1f - x);
        private static float EaseInOut(float x) => x * x * (3f - 2f * x);
        private static float Bell(float x) => Mathf.Sin(Mathf.Clamp01(x) * Mathf.PI);

        // Stable pseudo-random 0..1 per particle, so a motion looks the same every time.
        private static float Hash(int i, int salt)
        {
            float v = Mathf.Sin(i * 12.9898f + salt * 78.233f) * 43758.5453f;
            return v - Mathf.Floor(v);
        }
    }
}
