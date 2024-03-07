import React, { useState } from "react";
import { Implementation } from "../../data/parseReportData";
import CopyToClipboard from "../CopyToClipboard";
import Dialect from "../../data/Dialect";
import { complianceBadgeFor, versionsBadgeFor } from "../../data/Badge";
import { badgeFormatOptions, BadgeFormatOption } from "./BadgeFormats";

const EmbedBadges: React.FC<{
  implementation: Implementation;
}> = ({ implementation }) => {
  const [activeTab, setActiveTab] = useState("URL");
  const [activeBadge, setActiveBadge] = useState("JSON Schema Versions");
  const [badgeURI, setBadgeURI] = useState(
    versionsBadgeFor(implementation).href(),
  );

  const handleSelectBadge = (badgeName: string, URI: string): void => {
    setActiveBadge(badgeName);
    setBadgeURI(URI);
  };

  const handleSelectTab = (tabKey: string | null) => {
    if (tabKey) {
      setActiveTab(tabKey);
    }
  };

  const results = Object.entries(implementation.results).sort((a, b) => {
    return (
      Dialect.withName(b[0]).firstPublicationDate.getTime() -
      Dialect.withName(a[0]).firstPublicationDate.getTime()
    );
  });

  return (
    <div className="container dropdown px-0 col-12">
      <button
        className="btn btn-sm btn-success dropdown-toggle"
        type="button"
        data-bs-toggle="dropdown"
        data-bs-auto-close="outside"
        style={{ width: "80px" }}
      >
        Badges
      </button>
      <ul className="dropdown-menu mx-auto mb-3">
        <li>
          <div>
            <p className="text-center fw-semibold pt-2 pb-1 px-1 fs-6 mb-2 ">
              Generate Bowtie Badge
            </p>
            <div className="dropdown d-flex flex-column justify-content-center align-items-center px-2">
              <label className="pb-1" htmlFor="dropdownMenuButton">
                Available Badges
              </label>
              <button
                className="btn btn-sm btn-primary dropdown-toggle mx-auto"
                type="button"
                id="dropdownMenuButton"
                data-bs-toggle="dropdown"
                aria-expanded="false"
              >
                {activeBadge === "JSON Schema Versions"
                  ? "JSON Schema Versions"
                  : Dialect.withName(activeBadge).prettyName}
              </button>
              <ul
                className="dropdown-menu"
                aria-labelledby="dropdownMenuButton"
              >
                <h6 className="dropdown-header">Supported Dialects</h6>
                <li key={"JSON Schema Versions"}>
                  <button
                    className={`dropdown-item btn btn-sm ${
                      activeBadge === "JSON Schema Versions" ? "active" : ""
                    }`}
                    onClick={() =>
                      handleSelectBadge(
                        "JSON Schema Versions",
                        versionsBadgeFor(implementation).href(),
                      )
                    }
                  >
                    {"JSON Schema Versions"}
                  </button>
                </li>
                <h6 className="dropdown-header">Compliance Badges</h6>
                {/* sorting */}
                {results.map((result) => (
                  <li key={result[0]}>
                    <button
                      className={`dropdown-item btn btn-sm ${
                        result[0] === activeBadge ? "active" : ""
                      }`}
                      onClick={() =>
                        handleSelectBadge(
                          result[0],
                          complianceBadgeFor(
                            implementation,
                            Dialect.withName(result[0]),
                          ).href(),
                        )
                      }
                    >
                      {Dialect.withName(result[0]).prettyName}
                    </button>
                  </li>
                ))}
              </ul>
            </div>
          </div>
          <div className="container d-flex justify-content-center align-items-center flex-column pt-3">
            <ul className="nav nav-pills justify-content-center gap-1">
              {badgeFormatOptions.map(
                (formatItem: BadgeFormatOption, index) => {
                  return (
                    <li className="nav-item" key={index}>
                      <button
                        className={`nav-link btn btn-sm ${
                          activeTab === formatItem.type ? "active" : ""
                        }`}
                        onClick={() => handleSelectTab(formatItem.type)}
                      >
                        {formatItem.type}
                      </button>
                    </li>
                  );
                },
              )}
            </ul>
            <div className="tab-content mt-2 pt-2 pb-3">
              {badgeFormatOptions.map(
                (formatItem: BadgeFormatOption, index) => (
                  <div
                    key={index}
                    className={`tab-pane ${
                      activeTab === formatItem.type ? "active" : ""
                    } border rounded  pt-2 px-4 mx-2`}
                    style={{ width: "35vmin" }}
                  >
                    <div className="d-flex align-items-center justify-content-center px-1">
                      <div style={{ width: "100%" }}>
                        <span
                          className="font-monospace text-body-secondary fs-6 ps-2 d-block"
                          style={{
                            wordWrap: "break-word",
                            whiteSpace: "nowrap",
                            textOverflow: "hidden",
                            overflowX: "auto",
                            width: "100%",
                          }}
                        >
                          {formatItem.renderDisplay(badgeURI)}
                        </span>
                      </div>
                      <div className="ms-auto pb-2 px-1">
                        <CopyToClipboard
                          textToCopy={formatItem.generateCopyText(badgeURI)}
                        />
                      </div>
                    </div>
                  </div>
                ),
              )}
            </div>
          </div>
        </li>
      </ul>
    </div>
  );
};

export default EmbedBadges;
