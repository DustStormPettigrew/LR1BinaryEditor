using LibLR1.Utils;
using System;
using System.Collections.Generic;
using System.IO;

namespace LR1BinaryEditor
{
	static partial class Util
	{
		private static Dictionary<string, Dictionary<Token, string>> ms_keywordInfoBlocks;
		private static Dictionary<string, Dictionary<Token, string>> ms_keywordInfoProperties;

		public static void LoadKeywordInfo(string p_dirpath)
		{
			string path_blocks_cfg = Path.Combine(new string[] { p_dirpath, "blocks.cfg" });
			string path_props_cfg  = Path.Combine(new string[] { p_dirpath, "properties.cfg" });

			if (File.Exists(path_blocks_cfg))
			{
				ms_keywordInfoBlocks = LoadCfg(path_blocks_cfg);
			}
			else
			{
				ms_keywordInfoBlocks = new Dictionary<string, Dictionary<Token, string>>();
			}

			if (File.Exists(path_blocks_cfg))
			{
				ms_keywordInfoProperties = LoadCfg(path_props_cfg);
			}
			else
			{
				ms_keywordInfoProperties = new Dictionary<string, Dictionary<Token, string>>();
			}
		}

		private static Dictionary<string, Dictionary<Token, string>> LoadCfg(string p_filepath)
		{
			var output = new Dictionary<string, Dictionary<Token, string>>();

			string[] lines = File.ReadAllLines(p_filepath);
			foreach (string line in lines)
			{
				string l = line.Trim();
				if (l.Length == 0) continue;
				string src = l.Substring(0, l.IndexOf(' '));
				string type = src.Substring(0, src.IndexOf('.'));
				Token keyword = (Token)Convert.ToByte(src.Substring(type.Length + 1, 2), 16);

				if (!output.ContainsKey(type))
				{
					output[type] = new Dictionary<Token, string>();
				}
				output[type][keyword] = l.Substring(l.IndexOf(' ') + 1);
			}

			return output;
		}

		private static readonly Dictionary<string, string> k_fileFormats = new Dictionary<string, string>() {
			{ "ADB", "Skeletal Animation" },
			{ "BDB", "" },
			{ "BVB", "Collision Mesh" },
			{ "CCB", "NPC Car List" },
			{ "CDB", "Cutscene" },
			{ "CEB", "Cutscene events" },
			{ "CMB", "Chassis" },
			{ "CPB", "Checkpoint Layout" },
			{ "CRB", "Circuit Listing" },
			{ "DDB", "NPC Driver List" },
			{ "EMB", "Particle Emitter List" },
			{ "EVB", "" },
			{ "FDB", "Font Database" },
			{ "GCB", "" },
			{ "GDB", "3D Model" },
			{ "GHB", "Time-Trial Ghost Path" },
			{ "HZB", "" },
			{ "IDB", "2D Image List" },
			{ "LEB", "Lego Brick List" },
			{ "LSB", "Loading Screen Layout" },
			{ "MAB", "Material Animations" },
			{ "MDB", "Material List" },
			{ "MIB", "Menu Interface Layout" },
			{ "MSB", "" },
			{ "PCB", "Part Configuration" },
			{ "PWB", "Powerup Layout" },
			{ "RAB", "Track header" },
			{ "RCB", "Race Listing" },
			{ "RRB", "NPC Path" },
			{ "SDB", "Skeleton Structure" },
			{ "SKB", "Skybox Gradient" },
			{ "SPB", "Start Positions" },
			{ "TDB", "Texture List" },
			{ "TGB", "Trigger Boxes" },
			{ "TIB", "" },
			{ "TMB", "Material Physics Properties" },
			{ "TRB", "" },
			{ "WDB", "3D Scene" },
		};
	}
}