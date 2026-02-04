import fs from "fs";

const plots = JSON.parse(fs.readFileSync("./data/plots.json", "utf-8"));
const districts = [339, 340, 341, 641, 979];

for (const district of districts) {
  for (let division = 1; division <= 2; division++) {
    const entry = plots.find(
      (p: any) => p.District === district && p.Division === division
    );
    if (!entry) {
      console.log(
        `Failed to find entry for District ${district} Division ${division}`
      );
      continue;
    }

    const divisionMin = division === 1 ? 0 : 30;
    const divisionMax = division === 1 ? 30 : 60;
    for (let plot = divisionMin; plot < divisionMax; plot++) {
      const plotEntry = entry.Plots.find((p: any) => p.PlotId === plot);
      if (!plotEntry) {
        console.log(
          `Failed to find entry for District ${district} Division ${division} Plot ${
            plot + 1
          }`
        );
        continue;
      }
      if (
        plotEntry.C1.X == 0 ||
        plotEntry.C2.X == 0 ||
        plotEntry.C3.X == 0 ||
        plotEntry.C4.X == 0
      ) {
        console.log(
          `Found plot entry with empty corner for District ${district} Division ${division} Plot ${
            plot + 1
          }`
        );
        continue;
      }
    }
  }
}

const sortedPlots = plots
  .sort((a: any, b: any) => {
    if (a.District !== b.District) {
      return a.District - b.District;
    }
    if (a.Division !== b.Division) {
      return a.Division - b.Division;
    }
    return 0;
  })
  .map((district: any) => {
    district.Plots.sort(
      (plotA: any, plotB: any) => plotA.PlotId - plotB.PlotId
    );
    return district;
  });

fs.writeFileSync("./data/plots.json", JSON.stringify(sortedPlots));
