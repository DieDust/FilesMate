using FilesMate.FlowPlugin;

var request = FlowPluginHost.ReadRequest(args);
Console.Write(FlowPluginHost.Handle(request));
