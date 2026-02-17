using System;
using System.Collections.Generic;
using UnityEngine;

namespace CrowSave.Flags.IO
{
    [Serializable]
    public sealed class FlagsIORoute
    {
        [SerializeField] private string name = "Route";

        [Header("Input")]
        [SerializeReference] private Inputs.FlagsInputModule input;

        [Header("Outputs")]
        [SerializeReference] private List<Outputs.FlagsOutputModule> outputs = new List<Outputs.FlagsOutputModule>();

        public string Name => name ?? "";
        public Inputs.FlagsInputModule Input => input;
        public List<Outputs.FlagsOutputModule> Outputs => outputs;
    }
}
