using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using TAS.Utils;

namespace TAS.Input.Commands;

public partial record Command {
    public readonly string[] Args;
    public readonly TasCommandAttribute Attribute;
    private readonly Action commandCall; // null if ExecuteAtStart = true
    public readonly string FilePath;
    public readonly int Frame;
    public readonly int StudioLineNumber; // form zero

    private Command(TasCommandAttribute attribute, int frame, Action commandCall, string[] args, string filePath, int studioLineNumber) {
        Attribute = attribute;
        Frame = frame;
        this.commandCall = commandCall;
        Args = args;
        FilePath = filePath;
        StudioLineNumber = studioLineNumber;
    }

    public string LineText => Args.Length == 0 ? Attribute.Name : $"{Attribute.Name}, {string.Join(", ", Args)}";

    public void Invoke() => commandCall?.Invoke();
    public bool Is(string commandName) => Attribute.IsName(commandName);
}

public partial record Command {
    private static readonly object[] EmptyParameters = { };
    private static readonly Regex CheckSpaceRegex = new(@"^[^,]+?\s+[^,]", RegexOptions.Compiled);
    private static readonly Regex SpaceRegex = new(@"\s+", RegexOptions.Compiled);

    private static string[] Split(string line) {
        string trimLine = line.Trim();
        // Determined by the first separator
        string[] args = CheckSpaceRegex.IsMatch(trimLine) ? SpaceRegex.Split(trimLine) : trimLine.Split(',');
        return args.Select(text => text.Trim()).ToArray();
    }

    public static bool TryParse(InputController inputController, string filePath, int fileLine, string lineText, int frame, int studioLine,
        out Command command) {
        command = null;
        try {
            if (!string.IsNullOrEmpty(lineText) && char.IsLetter(lineText[0])) {
                string[] args = Split(lineText);
                string commandName = args[0];

                KeyValuePair<TasCommandAttribute, MethodInfo> pair = TasCommandAttribute.FindMethod(commandName);
                if (pair.Equals(default)) {
                    return false;
                }

                MethodInfo method = pair.Value;
                TasCommandAttribute attribute = pair.Key;

                string[] commandArgs = args.Skip(1).ToArray();

                ParameterInfo[] parameterInfos = method.GetParameters();
                object[] parameters = parameterInfos.Length switch {
                    4 => new object[] {commandArgs, studioLine, filePath, fileLine},
                    3 => new object[] {commandArgs, studioLine, filePath},
                    2 when parameterInfos[1].ParameterType == typeof(int) => new object[] {commandArgs, studioLine},
                    2 when parameterInfos[1].ParameterType == typeof(string) => new object[] {commandArgs, lineText.Trim()},
                    1 => new object[] {commandArgs},
                    0 => EmptyParameters,
                    _ => throw new ArgumentException()
                };

                Action commandCall = () => method.Invoke(null, parameters);
                command = new(attribute, frame, commandCall, commandArgs, filePath, studioLine);

                if (attribute.ExecuteTiming == ExecuteTiming.Parse) {
                    commandCall.Invoke();
                }

                if (!inputController.Commands.ContainsKey(frame)) {
                    inputController.Commands[frame] = new List<Command>();
                }

                inputController.Commands[frame].Add(command);

                return true;
            }

            return false;
        } catch (Exception e) {
            e.LogException("Failed to parse command.");
            return false;
        }
    }
}