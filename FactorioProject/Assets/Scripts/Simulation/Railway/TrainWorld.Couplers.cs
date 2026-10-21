using UnityEngine;

namespace ProjectF.Railway
{
    public sealed partial class TrainWorld
    {
        private TrainCouplerPresenter couplerPresenter;

        partial void AttachCouplerView(TrainInstance instance, Train view)
        {
            if (!Application.isPlaying) return;
            if (couplerPresenter == null) couplerPresenter = TrainCouplerPresenter.Create();
            couplerPresenter.Register(instance, view.CouplerRig);
        }

        partial void DetachCouplerView(TrainInstance instance)
        {
            if (couplerPresenter != null) couplerPresenter.Unregister(instance);
        }

        partial void ClearCouplerViews()
        {
            if (couplerPresenter != null) couplerPresenter.Release();
            couplerPresenter = null;
        }

        partial void RefreshCouplerConnection(TrainInstance first, TrainInstance second)
        {
            if (couplerPresenter != null) couplerPresenter.RefreshConnection(first, second);
        }
    }
}
