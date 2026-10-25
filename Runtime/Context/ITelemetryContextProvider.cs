namespace TestingFloorSdk {
    public interface ITelemetryContextProvider {
        void FillSnapshot(ref ContextSnapshot snapshot);
    }
}
