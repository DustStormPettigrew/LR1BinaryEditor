using System;
using System.Collections.Generic;
using System.IO;

namespace LR1BinaryEditor {
    static partial class Util {
        public static void LoadKeywordInfo(string dir) {
            string path_blocks_cfg = Path.Combine(new string[] { dir, "blocks.cfg" });
            string path_props_cfg = Path.Combine(new string[] { dir, "properties.cfg" });

            if (File.Exists(path_blocks_cfg))
                KeywordInfoBlocks = LoadCfg(path_blocks_cfg);
            else
                KeywordInfoBlocks = new Dictionary<string, Dictionary<byte, string>>();

            if (File.Exists(path_blocks_cfg))
                KeywordInfoProperties = LoadCfg(path_props_cfg);
            else
                KeywordInfoProperties = new Dictionary<string, Dictionary<byte, string>>();
        }

        private static Dictionary<string, Dictionary<byte, string>> LoadCfg(string file) {
            Dictionary<string, Dictionary<byte, string>> output = new Dictionary<string, Dictionary<byte, string>>();

            string[] lines = File.ReadAllLines(file);
            foreach (string line in lines) {
                string l = line.Trim();
                if (l.Length == 0) continue;
                string src = l.Substring(0, l.IndexOf(' '));
                string type = src.Substring(0, src.IndexOf('.'));
                byte keyword = Convert.ToByte(src.Substring(type.Length + 1, 2), 16);

                if (!output.ContainsKey(type))
                    output[type] = new Dictionary<byte, string>();
                output[type][keyword] = l.Substring(l.IndexOf(' ') + 1);
            }

            return output;
        }

        private static Dictionary<string, Dictionary<byte, string>> KeywordInfoBlocks;
        private static Dictionary<string, Dictionary<byte, string>> KeywordInfoProperties;

        private static readonly Dictionary<string, string> FILE_FORMATS = new Dictionary<string, string>() {
	        { "ADB", "Skeletal Animation" },
	        { "BDB", "" },
	        { "BVB", "Collision Mesh" },
	        { "CCB", "NPC Car List" },
	        { "CDB", "Cutscene" },
	        { "CEB", "Cutscene events" },
	        { "CMB", "" },
	        { "CPB", "Checkpoint Layout" },
	        { "CRB", "Circuit Listing" },
	        { "DDB", "NPC Driver List" },
	        { "EMB", "Particle Emitter List" },
	        { "EVB", "" },
	        { "FDB", "" },
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
	        { "PCB", "" },
	        { "PWB", "Powerup Layout" },
	        { "RAB", "Track header" },
	        { "RCB", "Race Listing" },
	        { "RRB", "NPC Path" },
	        { "SDB", "Skeleton Structure" },
	        { "SKB", "Skybox Gradient" },
	        { "SPB", "Start Positions" },
	        { "TDB", "Texture List" },
	        { "TGB", "" },
	        { "TIB", "" },
	        { "TMB", "Material Physics Properties" },
	        { "TRB", "" },
	        { "WDB", "3D Scene" },
        };
    }
}
