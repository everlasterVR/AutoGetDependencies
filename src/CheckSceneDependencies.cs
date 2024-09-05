#define ENV_DEVELOPMENT
using System;

namespace everlaster
{
    sealed class CheckSceneDependencies : Script
    {
        public override bool ShouldIgnore() => false;
        protected override bool useVersioning => true;

        public override void Init()
        {
            try
            {
                logBuilder = new LogBuilder(nameof(CheckSceneDependencies));
                if(!IsValidAtomType("Person") || IsDuplicate())
                {
                    return;
                }

                initialized = true;
            }
            catch(Exception e)
            {
                logBuilder.Exception(e);
            }
        }

        protected override void DoEnable()
        {
        }

        protected override void DoDisable()
        {
        }

        protected override void DoDestroy()
        {
        }
    }
}
