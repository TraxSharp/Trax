-- Add 'dormant_dependent' value to schedule_type enum for dependents that only fire
-- when explicitly activated at runtime via IDormantDependentContext
ALTER TYPE trax.schedule_type ADD VALUE IF NOT EXISTS 'dormant_dependent';
