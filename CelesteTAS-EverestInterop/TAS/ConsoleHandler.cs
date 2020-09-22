﻿using System;
using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework;
using Monocle;
using System.Threading;

namespace TAS {
	public class ConsoleHandler {
		public static void ExecuteCommand(string[] command) {
			string[] args = InputCommands.TrimArray(command, 1);
			string commandID = command[0].ToLower();
			if (commandID == "load" || commandID == "hard" || commandID == "rmx2")
				LoadCommand(commandID, args);
			else
				Engine.Commands.ExecuteCommand(commandID, args);
		}

		private static void LoadCommand(string command, string[] args) {
			try {
				if (SaveData.Instance == null || (!Manager.allowUnsafeInput && SaveData.Instance.FileSlot != -1)) {
					int slot = SaveData.Instance == null ? -1 : SaveData.Instance.FileSlot;
					SaveData data = UserIO.Load<SaveData>(SaveData.GetFilename(slot));
					SaveData.Start(data, -1);
				}

				AreaMode mode = AreaMode.Normal;
				if (command == "hard")
					mode = AreaMode.BSide;
				else if (command == "rmx2")
					mode = AreaMode.CSide;
				int levelID = GetLevelID(args[0]);

				if (args.Length > 1) {
					if (!int.TryParse(args[1], out int x)) {
						string screen = args[1];
						if (screen.StartsWith("lvl_"))
							screen = screen.Substring(4);
						if (args.Length > 2) {
							int checkpoint = int.Parse(args[2]);
							Load(mode, levelID, screen, checkpoint);
						}
						else {
							Load(mode, levelID, screen);
						}
					}

					else if (args.Length > 2) {
						int y = int.Parse(args[2]);
						Load(mode, levelID, new Vector2(x, y));
					}
				} 
				else {
					Load(mode, levelID);
				}
			}
			catch { }
		}

		private static int GetLevelID(string ID) {
			if (int.TryParse(ID, out int num))
				return num;
			else
				return AreaDataExt.Get(ID).ID;
		}

		private static void Load(AreaMode mode, int levelID, string screen = null, int checkpoint = 0) {
			Session session = new Session(new AreaKey(levelID, mode));
			if (screen != null) {
				session.Level = screen;
				session.FirstLevel = false;
			}
			if (checkpoint != 0) {
				LevelData levelData = session.MapData.Get(screen);
				Manager.controller.resetSpawn = levelData.Spawns[checkpoint];
			}
			Engine.Scene = new LevelLoader(session);
		}

		private static void Load(AreaMode mode, int levelID, Vector2 spawnPoint) {
			Session session = new Session(new AreaKey(levelID, mode));
			session.Level = session.MapData.GetAt(spawnPoint)?.Name;
			session.FirstLevel = false;
			Manager.controller.resetSpawn = spawnPoint;
			Engine.Scene = new LevelLoader(session);
		}

		public static string CreateConsoleCommand() {
			if (!(Engine.Scene is Level level))
				return null;
			AreaKey area = level.Session.Area;
			string mode = null;
			switch (area.Mode) {
				case AreaMode.Normal:
					mode = "load";
					break;
				case AreaMode.BSide:
					mode = "hard";
					break;
				case AreaMode.CSide:
					mode = "rmx2";
					break;
			}
			string ID = area.ID <= 10 ? area.ID.ToString() : area.GetSID();
			string location = null;
			Player player = level.Entities.FindFirst<Player>();
			if (player == null)
				location = level.Session.Level;
			else
				location = player.X.ToString() + " " + player.Y.ToString();
			return $"console {mode} {ID} {location}";

		}

		[Command("giveberry", "Gives player a red berry")]
		private static void CmdGiveBerry() {
			Level level = Engine.Scene as Level;
			if (level != null) {
				Player entity = level.Tracker.GetEntity<Player>();
				if (entity != null) {
					EntityData entityData = new EntityData();
					entityData.Position = entity.Position + new Vector2(0f, -16f);
					entityData.ID = Calc.Random.Next();
					entityData.Name = "strawberry";
					EntityID gid = new EntityID(level.Session.Level, entityData.ID);
					Strawberry entity2 = new Strawberry(entityData, Vector2.Zero, gid);
					level.Add(entity2);
				}
			}
		}

		[Command("clrsav", "clears save data on debug file")]
		private static void CmdClearSave() {
			SaveData.TryDelete(-1);
			SaveData.Start(new SaveData { Name = "debug" }, -1);
			// Pretend that we've beaten Prologue.
			LevelSetStats stats = SaveData.Instance.GetLevelSetStatsFor("Celeste");
			stats.UnlockedAreas = 1;
			stats.AreasIncludingCeleste[0].Modes[0].Completed = true;
		}
	}
}
