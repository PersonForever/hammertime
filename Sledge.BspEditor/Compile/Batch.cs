﻿using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using LogicAndTrick.Oy;
using Sledge.BspEditor.Documents;

namespace Sledge.BspEditor.Compile
{
    public class Batch
    {
        public List<BatchStep> Steps { get; set; }
        public Dictionary<string, string> Variables { get; set; }
        public bool Successful { get; set; }
        private bool _continue = true;

        public Batch()
        {
            Steps = new List<BatchStep>();
            Variables = new Dictionary<string, string>();
            Successful = true;
			Oy.Subscribe("Compile:Interrupt", () => _continue = false);

		}

		public async Task Run(MapDocument document)
        {
            await Oy.Publish("Compile:Started", this);

            foreach (var step in Steps)
            {
                if (!_continue) break;
                try
                {
                    await step.Run(this, document);
                }
                catch
                {
                    Successful = false;
                    throw;
                }
            }

            await Oy.Publish("Compile:Finished", this);
        }
    }
}
