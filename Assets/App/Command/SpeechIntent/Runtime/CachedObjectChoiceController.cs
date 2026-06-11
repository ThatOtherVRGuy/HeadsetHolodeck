using System.Collections.Generic;
using Holodeck.Direct;
using UnityEngine;

namespace SpeechIntent
{
    public sealed class CachedObjectChoiceController : MonoBehaviour
    {
        public bool HasPendingChoice { get; private set; }
        public VoiceIntentCommand PendingCommand { get; private set; }
        public SpatialSnapshot PendingSpatial { get; private set; }
        public List<CachedObjectRecord> PendingMatches { get; private set; } = new List<CachedObjectRecord>();

        public void BeginChoice(VoiceIntentCommand command, SpatialSnapshot spatial, List<CachedObjectRecord> matches)
        {
            Cancel();

            if (command == null || matches == null || matches.Count == 0)
                return;

            PendingCommand = command;
            PendingSpatial = CloneSpatialSnapshot(spatial);
            PendingMatches = new List<CachedObjectRecord>();
            foreach (CachedObjectRecord match in matches)
            {
                if (match != null)
                    PendingMatches.Add(match);
            }

            HasPendingChoice = PendingMatches.Count > 0;
            if (!HasPendingChoice)
                Cancel();
        }

        public bool TryConsumeUseSaved(out VoiceIntentCommand command, out SpatialSnapshot spatial, out CachedObjectRecord record)
        {
            CachedObjectRecord selected = HasPendingChoice && PendingMatches.Count > 0 ? PendingMatches[0] : null;
            return TryConsumeUseSaved(selected, out command, out spatial, out record);
        }

        public bool TryConsumeUseSaved(CachedObjectRecord selectedRecord, out VoiceIntentCommand command, out SpatialSnapshot spatial, out CachedObjectRecord record)
        {
            command = PendingCommand;
            spatial = PendingSpatial;
            record = HasPendingChoice && selectedRecord != null
                ? FindPendingMatch(selectedRecord)
                : null;

            bool success = command != null && record != null;
            if (success)
                Cancel();

            return success;
        }

        CachedObjectRecord FindPendingMatch(CachedObjectRecord selectedRecord)
        {
            if (selectedRecord == null)
                return null;

            foreach (CachedObjectRecord match in PendingMatches)
            {
                if (match == null)
                    continue;

                if (ReferenceEquals(match, selectedRecord))
                    return match;

                if (!string.IsNullOrWhiteSpace(match.object_id) &&
                    string.Equals(match.object_id, selectedRecord.object_id, System.StringComparison.Ordinal))
                    return match;
            }

            return null;
        }

        public bool TryConsumeCreateNew(out VoiceIntentCommand command, out SpatialSnapshot spatial)
        {
            command = PendingCommand;
            spatial = PendingSpatial;

            bool success = HasPendingChoice && command != null;
            if (success)
                Cancel();

            return success;
        }

        public void Cancel()
        {
            HasPendingChoice = false;
            PendingCommand = null;
            PendingSpatial = null;
            PendingMatches.Clear();
        }

        static SpatialSnapshot CloneSpatialSnapshot(SpatialSnapshot source)
        {
            if (source == null)
                return null;

            return new SpatialSnapshot
            {
                left_hand = CloneHand(source.left_hand),
                right_hand = CloneHand(source.right_hand),
                has_hand_midpoint = source.has_hand_midpoint,
                hand_midpoint = source.hand_midpoint,
                head_position = source.head_position,
                head_forward = source.head_forward
            };
        }

        static HandRaySnapshot CloneHand(HandRaySnapshot source)
        {
            if (source == null)
                return new HandRaySnapshot();

            return new HandRaySnapshot
            {
                source_name = source.source_name,
                is_available = source.is_available,
                is_pointing = source.is_pointing,
                pointing_confidence = source.pointing_confidence,
                distance_from_head = source.distance_from_head,
                forward_alignment = source.forward_alignment,
                origin = source.origin,
                direction = source.direction,
                has_hit = source.has_hit,
                hit_point = source.hit_point,
                hit_normal = source.hit_normal,
                hit_object_name = source.hit_object_name,
                hit_object_path = source.hit_object_path,
                hit_root_name = source.hit_root_name
            };
        }
    }
}
