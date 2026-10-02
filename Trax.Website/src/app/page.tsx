import Hero from "@/components/landing/Hero";
import StepShape from "@/components/landing/StepShape";
import FourWays from "@/components/landing/FourWays";
import RunRecord from "@/components/landing/RunRecord";
import LayerDiagram from "@/components/landing/LayerDiagram";
import DeploymentArchitectures from "@/components/landing/DeploymentArchitectures";
import WhereTraxStops from "@/components/landing/WhereTraxStops";
import QuickStart from "@/components/landing/QuickStart";
import PackageList from "@/components/landing/PackageList";

export default function Home() {
  return (
    <main>
      <Hero />
      <StepShape />
      <FourWays />
      <RunRecord />
      <LayerDiagram />
      <DeploymentArchitectures />
      <WhereTraxStops />
      <QuickStart />
      <PackageList />
    </main>
  );
}
