import Hero from "@/components/landing/Hero";
import ProblemSolution from "@/components/landing/ProblemSolution";
import LayerDiagram from "@/components/landing/LayerDiagram";
import FeatureGrid from "@/components/landing/FeatureGrid";
import DeploymentArchitectures from "@/components/landing/DeploymentArchitectures";
import QuickStart from "@/components/landing/QuickStart";
import PackageList from "@/components/landing/PackageList";

export default function Home() {
  return (
    <>
      <Hero />
      <ProblemSolution />
      <LayerDiagram />
      <FeatureGrid />
      <DeploymentArchitectures />
      <QuickStart />
      <PackageList />
    </>
  );
}
