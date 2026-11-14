namespace SwapMid.DataIntegration;

public sealed partial class DataPipelineExecutionCoordinator
{
    private static PipelineTerminalStoreTopologyPhase
        GetResolutionTopologyPhase(PipelineExecutionResolution resolution)
        => resolution switch
        {
            PipelineExecutionResolution.ConfirmedApplied =>
                PipelineTerminalStoreTopologyPhase.OutcomeResolutionApplied,
            PipelineExecutionResolution.ConfirmedNotApplied =>
                PipelineTerminalStoreTopologyPhase.OutcomeResolution,
            _ => throw ResolutionInvalid(),
        };
}
