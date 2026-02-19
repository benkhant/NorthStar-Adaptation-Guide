# NorthStar Adaptation Guide (Work In Progress)
### A Practical Guide to Building and Documenting a Low-Cost Open-Source Optical See-Through AR Headset

This repository documents the adaptation of Project North Star into a low-cost, reproducible, and well-documented optical see-through augmented reality (OST-AR) headset for computing education. The project establishes a transparent hardware and calibration foundation that supports future development of interactive AR learning software.

The primary focus of this project is not only building functional hardware, but systematically documenting the entire build process to lower financial and technical barriers to classroom deployment and future AR software development.

---

## Why This Project?

Commercial optical see-through AR headsets remain costly and closed-source, limiting classroom adoption. This project aims to lower those barriers by providing a transparent, replicable, and well-documented alternative for educational use.

---

## Project Goals

- Adapt an open-source AR headset for educational use  
- Provide clear mechanical assembly documentation  
- Develop a repeatable optics calibration workflow  
- Publish a transparent bill of materials with cost breakdown  
- Enable reproducibility for educators and researchers  

---

## Hardware Assembly

This guide is still under development. Headstrap assembly section coming soon.

Mechanical assembly instructions are available here:

[Hardware Assembly Guide](docs/hardware-assembly.md)

Includes:
- Required components
- Heat insert installation
- Cable routing
- Display mounting
- Troubleshooting notes

---

## Optics Calibration

Calibration documentation is available here:

 [Optics Calibration Guide](docs/optics-calibration.md)

Includes:
- Checkerboard-based alignment procedure
- Distortion correction process
- Representative calibration results
- Troubleshooting guide

---

## 3D-Printable Components

All printable components are located in:

[3D Printing Files](3d-printing-stl)

Each component is provided as an STL file for direct printing.

---

## Cost Transparency

This build prioritizes affordability using:
- Commodity electronics
- 3D-printed structural parts
- Open-source design

Filament costs are calculated using:
- Slicer-reported filament weight (grams)
- Cost per gram (based on spool price)
- Total print cost per component

A detailed bill of materials and cost breakdown spreadsheet will be published in this repository.

---

## Current Status

- ✅ Hardware prototype constructed
- ✅ Optics calibration workflow established  
- 🚧 Mechanical documentation in progress  
- 🚧 Interactive AR educational software in development  
- 🚧 Classroom pilot evaluation planned  

---

## Software (Next Phase)

The current prototype operates as a secondary display driven by a host laptop. Development is underway to integrate interactive AR visualizations for computing education, beginning with:

- Quicksort partition step walkthrough
- Linked list pointer updates

Future work will include classroom pilots and evaluation of learning impact.

---

## Acknowledgements

Inspired by Project North Star.

Developed at Bucknell University.
