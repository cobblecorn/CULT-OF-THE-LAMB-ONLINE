using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace COTLOnline.Diagnostics
{
    internal sealed class DamageEventState
    {
        public int VictimId;
        public string VictimName;
        public string VictimTeam;
        public float VictimHpBefore;
        public float VictimHpTotal;
        public int AttackerId;
        public string AttackerName;
        public bool AttackerIsPlayer;
        public Vector3 AttackerPosition;
        public float Damage;
        public Vector3 AttackLocation;
        public Health.AttackTypes AttackType;
        public Health.AttackFlags AttackFlags;
        public bool Immediate;
    }

    internal static class SyncEventRecorder
    {
        private static bool _enabled;
        private static bool _appliedDamageOnly = true;
        private static bool _ignoreNeutralDamage = true;
        private static bool _playerMotionStream = true;
        private static bool _playerInputStream = true;
        private static float _playerMotionInterval = 0.2f;
        private static float _playerInputInterval = 0.05f;
        private static float _nextPlayerMotionAt;
        private static float _nextPlayerInputAt;
        private static int _playerMotionSequence;
        private static int _playerInputSequence;
        private static int _playerLifeSequence;
        private static bool _pendingAttackDown;
        private static bool _pendingAttackUp;
        private static bool _pendingDodgeDown;
        private static bool _pendingCurseDown;
        private static bool _pendingCurseUp;
        private static bool _pendingHeavyDown;
        private static string _clientId = "unknown";
        private static string _sessionId = "unknown";
        private static readonly Dictionary<int, string> LastPlayerLifeSignatures = new Dictionary<int, string>();

        public static void Configure(bool enabled, bool appliedDamageOnly, bool ignoreNeutralDamage, bool playerMotionStream, float playerMotionInterval, bool playerInputStream, float playerInputInterval, string clientId, string sessionId)
        {
            _enabled = enabled;
            _appliedDamageOnly = appliedDamageOnly;
            _ignoreNeutralDamage = ignoreNeutralDamage;
            _playerMotionStream = playerMotionStream;
            _playerInputStream = playerInputStream;
            _playerMotionInterval = Mathf.Max(0.05f, playerMotionInterval);
            _playerInputInterval = Mathf.Max(0.025f, playerInputInterval);
            _nextPlayerMotionAt = 0f;
            _nextPlayerInputAt = 0f;
            _playerMotionSequence = 0;
            _playerInputSequence = 0;
            _playerLifeSequence = 0;
            LastPlayerLifeSignatures.Clear();
            _pendingAttackDown = false;
            _pendingAttackUp = false;
            _pendingDodgeDown = false;
            _pendingCurseDown = false;
            _pendingCurseUp = false;
            _pendingHeavyDown = false;
            _clientId = Clean(clientId);
            _sessionId = Clean(sessionId);
            WorldTrace.Record(
                "sync.config",
                Identity()
                + " enabled=" + enabled
                + " appliedDamageOnly=" + appliedDamageOnly
                + " ignoreNeutralDamage=" + ignoreNeutralDamage
                + " playerMotionStream=" + playerMotionStream
                + " playerMotionInterval=" + WorldTrace.FormatFloat(_playerMotionInterval)
                + " playerInputStream=" + playerInputStream
                + " playerInputInterval=" + WorldTrace.FormatFloat(_playerInputInterval));
        }

        public static void RecordScene(string reason, string sceneName, string detail)
        {
            if (!_enabled)
            {
                return;
            }

            SafeRecord("sync.scene", () =>
                Identity()
                + " reason=" + reason
                + " scene=" + Clean(sceneName)
                + " detail=" + Clean(detail)
                + " location=" + Clean(Convert.ToString(PlayerFarming.Location)));
        }

        public static void RecordPlayerState(Scene scene)
        {
            if (!_enabled)
            {
                return;
            }

            SafeRun("sync.player_state", () =>
            {
                if (PlayerFarming.players == null)
                {
                    return;
                }

                for (int i = 0; i < PlayerFarming.players.Count; i++)
                {
                    PlayerFarming player = PlayerFarming.players[i];
                    if (player == null)
                    {
                        continue;
                    }

                    Health health = player.GetComponent<Health>();
                    WorldTrace.Record(
                        "sync.player_state",
                        Identity()
                        + " scene=" + Clean(scene.name)
                        + " location=" + Clean(Convert.ToString(PlayerFarming.Location))
                        + " room=" + Clean(RoomToken())
                        + " playerIndex=" + i
                        + " id=" + RuntimeId(player.gameObject)
                        + " playerID=" + player.playerID
                        + " name=" + Clean(player.gameObject != null ? player.gameObject.name : "null")
                        + " pos=" + WorldTrace.FormatVector(player.transform.position)
                        + " state=" + Clean(player.state != null ? player.state.CURRENT_STATE.ToString() : "null")
                        + " hp=" + HealthValue(health)
                        + " timeScale=" + WorldTrace.FormatFloat(Time.timeScale)
                        + " unscaledTime=" + WorldTrace.FormatFloat(Time.unscaledTime));
                }
            });
        }

        public static void TickPlayerMotion()
        {
            if (!_enabled || !_playerMotionStream)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now < _nextPlayerMotionAt)
            {
                return;
            }

            _nextPlayerMotionAt = now + _playerMotionInterval;
            RecordPlayerMotion(SceneManager.GetActiveScene(), now);
        }

        public static void TickPlayerInput()
        {
            if (!_enabled || !_playerInputStream)
            {
                return;
            }

            SafeRun("sync.player_input", () =>
            {
                PlayerFarming player = PlayerFarming.Instance;
                if (player == null)
                {
                    return;
                }

                _pendingAttackDown |= InputManager.Gameplay.GetAttackButtonDown(player);
                _pendingAttackUp |= InputManager.Gameplay.GetAttackButtonUp(player);
                _pendingDodgeDown |= InputManager.Gameplay.GetDodgeButtonDown(player);
                _pendingCurseDown |= InputManager.Gameplay.GetCurseButtonDown(player);
                _pendingCurseUp |= InputManager.Gameplay.GetCurseButtonUp(player);
                _pendingHeavyDown |= InputManager.Gameplay.GetHeavyAttackButtonDown(player);

                float now = Time.unscaledTime;
                if (now < _nextPlayerInputAt)
                {
                    return;
                }

                _nextPlayerInputAt = now + _playerInputInterval;
                RecordPlayerInput(SceneManager.GetActiveScene(), player, now);
                _pendingAttackDown = false;
                _pendingAttackUp = false;
                _pendingDodgeDown = false;
                _pendingCurseDown = false;
                _pendingCurseUp = false;
                _pendingHeavyDown = false;
            });
        }

        public static void TickPlayerLife()
        {
            if (!_enabled)
            {
                return;
            }

            SafeRun("sync.player_life", () =>
            {
                if (PlayerFarming.players == null)
                {
                    return;
                }

                Scene scene = SceneManager.GetActiveScene();
                for (int i = 0; i < PlayerFarming.players.Count; i++)
                {
                    PlayerFarming player = PlayerFarming.players[i];
                    if (player == null)
                    {
                        continue;
                    }

                    string state = player.state != null ? player.state.CURRENT_STATE.ToString() : "null";
                    Health health = player.GetComponent<Health>();
                    bool knockedOut = SafeBool(() => player.IsKnockedOut);
                    bool active = player.gameObject != null && player.gameObject.activeInHierarchy;
                    string result = SafeString(() => DataManager.Instance != null ? Convert.ToString(DataManager.Instance.LastRunResults) : "null");
                    string signature = state
                        + "|" + HealthValue(health)
                        + "|" + knockedOut
                        + "|" + active
                        + "|" + result
                        + "|" + SafeBool(() => PlayerFarming.AutoRespawn);
                    int runtimeId = RuntimeId(player.gameObject);

                    string previous;
                    if (LastPlayerLifeSignatures.TryGetValue(runtimeId, out previous)
                        && string.Equals(previous, signature, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    LastPlayerLifeSignatures[runtimeId] = signature;
                    int sequence = ++_playerLifeSequence;
                    WorldTrace.Record(
                        "sync.player_life",
                        Identity()
                        + " seq=" + sequence
                        + " scene=" + Clean(scene.name)
                        + " location=" + Clean(Convert.ToString(PlayerFarming.Location))
                        + " room=" + Clean(RoomToken())
                        + " playerIndex=" + i
                        + " id=" + runtimeId
                        + " playerID=" + player.playerID
                        + " name=" + Clean(player.gameObject != null ? player.gameObject.name : "null")
                        + " pos=" + WorldTrace.FormatVector(player.transform.position)
                        + " state=" + Clean(state)
                        + " hp=" + HealthValue(health)
                        + " knockedOut=" + knockedOut
                        + " active=" + active
                        + " isLamb=" + SafeBool(() => player.isLamb)
                        + " playersCount=" + SafeInt(() => PlayerFarming.playersCount)
                        + " autoRespawn=" + SafeBool(() => PlayerFarming.AutoRespawn)
                        + " lastRunResult=" + Clean(result)
                        + " timeScale=" + WorldTrace.FormatFloat(Time.timeScale)
                        + " unscaledTime=" + WorldTrace.FormatFloat(Time.unscaledTime));
                }
            });
        }

        private static void RecordPlayerInput(Scene scene, PlayerFarming player, float now)
        {
            if (player == null)
            {
                return;
            }

            Health health = player.GetComponent<Health>();
            int sequence = ++_playerInputSequence;
            float horizontal = InputManager.Gameplay.GetHorizontalAxis(player);
            float vertical = InputManager.Gameplay.GetVerticalAxis(player);
            bool attackHeld = InputManager.Gameplay.GetAttackButtonHeld(player);
            bool dodgeHeld = InputManager.Gameplay.GetDodgeButtonHeld(player);
            bool curseHeld = InputManager.Gameplay.GetCurseButtonHeld(player);
            bool heavyHeld = InputManager.Gameplay.GetHeavyAttackButtonHeld(player);
            PlayerSpells spells = player.playerSpells;
            FaithAmmo faithAmmo = spells != null ? spells.faithAmmo : null;
            string facingAngle = player.state != null ? WorldTrace.FormatFloat(player.state.facingAngle) : "unknown";
            string lookAngle = player.state != null ? WorldTrace.FormatFloat(player.state.LookAngle) : "unknown";
            string aimAngle = spells != null ? WorldTrace.FormatFloat(spells.AimAngle) : "unknown";
            string faith = faithAmmo != null ? WorldTrace.FormatFloat(faithAmmo.Ammo) : "unknown";
            string faithTotal = faithAmmo != null ? WorldTrace.FormatFloat(faithAmmo.Total) : "unknown";
            string faithCost = spells != null ? spells.AmmoCost.ToString() : "unknown";

            WorldTrace.RecordHighFrequency(
                "sync.player_input",
                Identity()
                + " seq=" + sequence
                + " scene=" + Clean(scene.name)
                + " location=" + Clean(Convert.ToString(PlayerFarming.Location))
                + " room=" + Clean(RoomToken())
                + " playerIndex=0"
                + " id=" + RuntimeId(player.gameObject)
                + " playerID=" + player.playerID
                + " name=" + Clean(player.gameObject != null ? player.gameObject.name : "null")
                + " ax=" + WorldTrace.FormatFloat(horizontal)
                + " ay=" + WorldTrace.FormatFloat(vertical)
                + " attackDown=" + _pendingAttackDown
                + " attackHeld=" + attackHeld
                + " attackUp=" + _pendingAttackUp
                + " dodgeDown=" + _pendingDodgeDown
                + " dodgeHeld=" + dodgeHeld
                + " curseDown=" + _pendingCurseDown
                + " curseHeld=" + curseHeld
                + " curseUp=" + _pendingCurseUp
                + " heavyDown=" + _pendingHeavyDown
                + " heavyHeld=" + heavyHeld
                + " facingAngle=" + facingAngle
                + " lookAngle=" + lookAngle
                + " aimAngle=" + aimAngle
                + " faithAmmo=" + faith
                + " faithTotal=" + faithTotal
                + " faithCost=" + faithCost
                + " pos=" + WorldTrace.FormatVector(player.transform.position)
                + " state=" + Clean(player.state != null ? player.state.CURRENT_STATE.ToString() : "null")
                + " hp=" + HealthValue(health)
                + " timeScale=" + WorldTrace.FormatFloat(Time.timeScale)
                + " unscaledTime=" + WorldTrace.FormatFloat(now));
        }

        private static void RecordPlayerMotion(Scene scene, float now)
        {
            SafeRun("sync.player_motion", () =>
            {
                if (PlayerFarming.players == null)
                {
                    return;
                }

                int sequence = ++_playerMotionSequence;
                for (int i = 0; i < PlayerFarming.players.Count; i++)
                {
                    PlayerFarming player = PlayerFarming.players[i];
                    if (player == null)
                    {
                        continue;
                    }

                    Health health = player.GetComponent<Health>();
                    WorldTrace.RecordHighFrequency(
                        "sync.player_motion",
                        Identity()
                        + " seq=" + sequence
                        + " scene=" + Clean(scene.name)
                        + " location=" + Clean(Convert.ToString(PlayerFarming.Location))
                        + " room=" + Clean(RoomToken())
                        + " playerIndex=" + i
                        + " id=" + RuntimeId(player.gameObject)
                        + " playerID=" + player.playerID
                        + " name=" + Clean(player.gameObject != null ? player.gameObject.name : "null")
                        + " pos=" + WorldTrace.FormatVector(player.transform.position)
                        + " state=" + Clean(player.state != null ? player.state.CURRENT_STATE.ToString() : "null")
                        + " hp=" + HealthValue(health)
                        + " timeScale=" + WorldTrace.FormatFloat(Time.timeScale)
                        + " unscaledTime=" + WorldTrace.FormatFloat(now));
                }
            });
        }

        private static string RoomToken()
        {
            try
            {
                return GameManager.IsDungeon(PlayerFarming.Location)
                    ? BridgeCombatAuthority.CurrentRoomKey()
                    : "none";
            }
            catch
            {
                return "unknown";
            }
        }

        public static void RecordWorldHash(string hash, int selectedFields)
        {
            if (!_enabled)
            {
                return;
            }

            SafeRecord("sync.world_hash", () =>
                Identity()
                + " hash=" + Clean(hash)
                + " selectedFields=" + selectedFields
                + " scene=" + Clean(SceneManager.GetActiveScene().name)
                + " location=" + Clean(Convert.ToString(PlayerFarming.Location))
                + " unscaledTime=" + WorldTrace.FormatFloat(Time.unscaledTime));
        }

        public static void RecordWorldIdentity(
            string hash,
            int selectedFields,
            int saveSlot,
            int run,
            int followersCount,
            int structuresCount,
            string cultName,
            string lastDungeonSeeds,
            string saveFileHash)
        {
            if (!_enabled)
            {
                return;
            }

            SafeRecord("sync.world_identity", () =>
                Identity()
                + " hash=" + Clean(hash)
                + " saveHash=" + Clean(saveFileHash)
                + " selectedFields=" + selectedFields
                + " saveSlot=" + saveSlot
                + " scene=" + Clean(SceneManager.GetActiveScene().name)
                + " location=" + Clean(Convert.ToString(PlayerFarming.Location))
                + " run=" + run
                + " followers=" + followersCount
                + " structures=" + structuresCount
                + " cultName=" + Clean(cultName)
                + " lastDungeonSeeds=" + Clean(lastDungeonSeeds)
                + " unscaledTime=" + WorldTrace.FormatFloat(Time.unscaledTime));
        }

        public static DamageEventState CaptureDamage(
            Health victim,
            float damage,
            GameObject attacker,
            Vector3 attackLocation,
            Health.AttackTypes attackType,
            Health.AttackFlags attackFlags,
            bool immediate)
        {
            if (!_enabled)
            {
                return null;
            }

            try
            {
                return new DamageEventState
                {
                    VictimId = RuntimeId(victim != null ? victim.gameObject : null),
                    VictimName = victim != null && victim.gameObject != null ? victim.gameObject.name : "null",
                    VictimTeam = victim != null ? victim.team.ToString() : "null",
                    VictimHpBefore = victim != null ? victim.HP : 0f,
                    VictimHpTotal = victim != null ? victim.totalHP : 0f,
                    AttackerId = RuntimeId(attacker),
                    AttackerName = attacker != null ? attacker.name : "null",
                    AttackerIsPlayer = IsPlayer(attacker),
                    AttackerPosition = attacker != null ? attacker.transform.position : Vector3.zero,
                    Damage = damage,
                    AttackLocation = attackLocation,
                    AttackType = attackType,
                    AttackFlags = attackFlags,
                    Immediate = immediate
                };
            }
            catch (Exception ex)
            {
                WorldTrace.Record("sync.damage.capture_error", ex.GetType().Name + ": " + ex.Message);
                return null;
            }
        }

        public static void RecordDamage(DamageEventState state, Health victim, bool applied)
        {
            if (!_enabled || state == null)
            {
                return;
            }

            if (_appliedDamageOnly && !applied)
            {
                return;
            }

            if (_ignoreNeutralDamage && string.Equals(state.VictimTeam, "Neutral", StringComparison.Ordinal))
            {
                return;
            }

            SafeRecord("sync.damage", () =>
                Identity()
                + " applied=" + applied
                + " scene=" + Clean(SceneManager.GetActiveScene().name)
                + " location=" + Clean(Convert.ToString(PlayerFarming.Location))
                + " victimId=" + state.VictimId
                + " victimName=" + Clean(state.VictimName)
                + " victimTeam=" + Clean(state.VictimTeam)
                + " hpBefore=" + WorldTrace.FormatFloat(state.VictimHpBefore)
                + " hpAfter=" + WorldTrace.FormatFloat(victim != null ? victim.HP : 0f)
                + " hpTotal=" + WorldTrace.FormatFloat(state.VictimHpTotal)
                + " attackerId=" + state.AttackerId
                + " attackerName=" + Clean(state.AttackerName)
                + " attackerIsPlayer=" + state.AttackerIsPlayer
                + " attackerPos=" + WorldTrace.FormatVector(state.AttackerPosition)
                + " damage=" + WorldTrace.FormatFloat(state.Damage)
                + " loc=" + WorldTrace.FormatVector(state.AttackLocation)
                + " type=" + state.AttackType
                + " flags=" + state.AttackFlags
                + " immediate=" + state.Immediate);
        }

        public static void RecordDeath(UnitObject unit, GameObject attacker, Vector3 attackLocation, Health victim, Health.AttackTypes attackType, Health.AttackFlags attackFlags)
        {
            if (!_enabled)
            {
                return;
            }

            SafeRecord("sync.death", () =>
                Identity()
                + " scene=" + Clean(SceneManager.GetActiveScene().name)
                + " location=" + Clean(Convert.ToString(PlayerFarming.Location))
                + " unitId=" + RuntimeId(unit != null ? unit.gameObject : null)
                + " unitName=" + Clean(unit != null && unit.gameObject != null ? unit.gameObject.name : "null")
                + " victimId=" + RuntimeId(victim != null ? victim.gameObject : null)
                + " victimName=" + Clean(victim != null && victim.gameObject != null ? victim.gameObject.name : "null")
                + " victimTeam=" + Clean(victim != null ? victim.team.ToString() : "null")
                + " hp=" + HealthValue(victim)
                + " attackerId=" + RuntimeId(attacker)
                + " attackerName=" + Clean(attacker != null ? attacker.name : "null")
                + " attackerIsPlayer=" + IsPlayer(attacker)
                + " attackerPos=" + WorldTrace.FormatVector(attacker != null ? attacker.transform.position : Vector3.zero)
                + " loc=" + WorldTrace.FormatVector(attackLocation)
                + " type=" + attackType
                + " flags=" + attackFlags);
        }

        private static void SafeRecord(string category, Func<string> buildMessage)
        {
            SafeRun(category, () => WorldTrace.Record(category, buildMessage()));
        }

        private static void SafeRun(string category, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                WorldTrace.Record(category + ".error", ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static int RuntimeId(GameObject obj)
        {
            return obj != null ? obj.GetInstanceID() : 0;
        }

        private static bool IsPlayer(GameObject obj)
        {
            return obj != null && obj.GetComponent<PlayerFarming>() != null;
        }

        private static string HealthValue(Health health)
        {
            if (health == null)
            {
                return "null";
            }

            return WorldTrace.FormatFloat(health.HP) + "/" + WorldTrace.FormatFloat(health.totalHP);
        }

        private static bool SafeBool(Func<bool> read)
        {
            try
            {
                return read();
            }
            catch
            {
                return false;
            }
        }

        private static int SafeInt(Func<int> read)
        {
            try
            {
                return read();
            }
            catch
            {
                return -1;
            }
        }

        private static string SafeString(Func<string> read)
        {
            try
            {
                return read() ?? "null";
            }
            catch (Exception ex)
            {
                return "err:" + ex.GetType().Name;
            }
        }

        private static string Clean(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "null";
            }

            return value.Replace(" ", "_").Replace("\t", "_").Replace("\r", "_").Replace("\n", "_");
        }

        private static string Identity()
        {
            return "clientId=" + _clientId + " sessionId=" + _sessionId;
        }
    }
}
