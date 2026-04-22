namespace TestingFloor {
    public interface ITelemetryContextProvider {
        void FillSnapshot(ref ContextSnapshot snapshot);
    }
}
