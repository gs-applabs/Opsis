using MotionDesk.Services;
if (args.Length != 2) { Console.Error.WriteLine("usage: seed|write|read <json-path>"); return 2; }
try
{
    if (args[0] == "seed") AtomicFile.WriteAllText(args[1], "{\"value\":\"old\"}");
    else if (args[0] == "write") AtomicFile.WriteAllText(args[1], "{\"value\":\"new\"}");
    else if (args[0] == "read") Console.WriteLine(AtomicFile.ReadAllText(args[1]));
    else return 2;
    return 0;
}
catch (Exception e) { Console.Error.WriteLine(e.GetType().Name + ": " + e.Message); return 1; }
