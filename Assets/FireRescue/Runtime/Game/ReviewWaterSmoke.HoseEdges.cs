using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace FireRescue
{
    // Extra coverage for the real reel path at a deliberately sparse presentation rate.
    // The broken bathroom door cannot be crossed during the fixed 1.30s reel from the
    // distant taut station, so this verifies the measurable low-frame-rate condition
    // without introducing a temporary obstacle or altering the scene.
    public sealed partial class ReviewWaterSmoke
    {
        IEnumerator VerifyHoseDoorEdges()
        {
            simulateFire = false;
            var tube = input.GetComponent<PhysicalHoseTube>();
            Need(hose.IsConnected && hose.BreakUnderTension && tube.IsVisible && !tube.IsReeling,
                "Low-rate reel probe starts from the real reconnected toilet hose");

            var source = hose.Source;
            Vector3 aim = new(-4.7f, .1f, -3.42f);
            yield return Walk(new Vector3(.45f, .1f, .18f));
            yield return Walk(new Vector3(.6f, .1f, -3.42f));
            yield return Walk(new Vector3(-2.84f, .1f, -3.42f));

            int blockedSpans = 0, inspectedFrames = 0;
            float maxAnchorError = 0f;
            void Inspect(float presentationDelta)
            {
                tube.TickPresentation(presentationDelta);
                if (!tube.IsVisible) return;
                inspectedFrames++;
                maxAnchorError = Mathf.Max(maxAnchorError,
                    Vector3.Distance(tube.NodePosition(tube.NodeCount - 1), hose.InletPosition));
                for (int i = 1; i < tube.NodeCount; i++)
                    if (tube.RenderedSpanIsBlocked(tube.NodePosition(i - 1), tube.NodePosition(i), tube.RenderRadius - .004f))
                        blockedSpans++;
            }

            for (int i = 0; i < 150 && !hose.IsAtLengthLimit; i++)
            {
                Step(Vector2.left, aim);
                if (i % 2 == 1) Inspect(2f * Dt);
                if (i % 4 == 3) yield return null;
            }
            Need(hose.IsAtLengthLimit && hose.IsConnected,
                "Low-rate reel probe reaches the unchanged 10m physical limit before it breaks");

            int pullTicks = 0;
            while (hose.IsConnected && pullTicks < 72)
            {
                Step(Vector2.left, aim);
                pullTicks++;
                if (pullTicks % 2 == 0) Inspect(2f * Dt);
                if (pullTicks % 4 == 0) yield return null;
            }
            Need(!hose.IsConnected && tube.IsReeling && pullTicks >= 38,
                $"The same sustained physical pull starts the low-rate reel ({pullTicks * Dt:F2}s)");

            Vector3 start = input.transform.position;
            Vector3 target = new(-1.10f, .1f, -3.42f);
            int reelFrames = 0;
            while (tube.IsReeling && reelFrames < 30)
            {
                Vector3 delta = target - input.transform.position;
                Vector2 movement = Vector2.ClampMagnitude(new Vector2(delta.x, delta.z) * 4f, 1f);
                // The motor continues to own travel and facing. The tube is intentionally
                // refreshed only every fourth simulation tick, equivalent to 15 Hz.
                for (int substep = 0; substep < 4; substep++)
                    Step(movement, input.transform.position + Vector3.right * .9f);
                Inspect(4f * Dt);
                reelFrames++;
                yield return null;
            }

            Need(!tube.IsReeling && !tube.IsVisible && Vector3.Distance(start, input.transform.position) > .25f,
                "A moving, turning player remains the reel anchor at 15Hz until the hose reaches the inlet");
            Need(maxAnchorError < .085f && blockedSpans == 0 && inspectedFrames > 0,
                $"15Hz reel keeps every inspected visible span clear and the inlet anchored (frames {inspectedFrames}, spans {blockedSpans}, anchor {maxAnchorError:F3}m)");
            Need(source == hose.Source || !hose.IsConnected,
                "Low-rate reel never switches to a different fluid source");
            File.AppendAllText(Path.Combine(output, "results.txt"),
                $"TUBE low-rate reel 15Hz frames={inspectedFrames}; blocked spans={blockedSpans}; maximum inlet error={maxAnchorError:F4}m. " +
                "The source-to-taut station exceeds the 1.30s reel travel budget, so crossing broken door.003 during one visible reel is not claimed by this probe.\n");
        }
    }
}
