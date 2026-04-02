#include "anti_flicker.h"

#include <algorithm>
#include <cmath>
#include <cstring>
#include <memory>
#include <limits>
#include <vector>

namespace
{
    struct MotionBlock
    {
        int Dx = 0;
        int Dy = 0;
        float Weight = 0.0f;
        float Reprojection = 1.0f;
    };

    enum class ContentMode
    {
        Video = 0,
        Anime = 1,
        Faces = 2,
        AnimeUltra = 3
    };

    enum class Algorithm
    {
        LumaStabilizer = 0,
        FlowGuided = 1
    };

    struct ModeTuning
    {
        float FlickerGain;
        float MotionGain;
        float EdgeGuardScale;
        float MaxBlendScale;
        float FlatBoost;
    };

    static int ClampInt(int value, int minValue, int maxValue)
    {
        return std::max(minValue, std::min(maxValue, value));
    }

    static float ClampFloat(float value, float minValue, float maxValue)
    {
        return std::max(minValue, std::min(maxValue, value));
    }

    static std::uint8_t Luma(std::uint8_t b, std::uint8_t g, std::uint8_t r)
    {
        return static_cast<std::uint8_t>((29 * b + 150 * g + 77 * r) >> 8);
    }

    class AntiFlickerContext final
    {
    public:
        explicit AntiFlickerContext(const af_config& config)
            : m_width(std::max(1, config.width))
            , m_height(std::max(1, config.height))
            , m_channels(config.channels == 4 ? 4 : 3)
            , m_factor(std::max(2, config.downscale_factor))
            , m_blockSize(std::max(16, config.block_size))
            , m_searchRadius(std::max(1, config.search_radius))
            , m_contentMode(static_cast<ContentMode>(std::clamp(config.content_mode, 0, 3)))
            , m_algorithm(static_cast<Algorithm>(std::clamp(config.algorithm, 0, 1)))
            , m_blendStrength(ClampFloat(config.blend_strength, 0.0f, 1.0f))
            , m_maxBlend(ClampFloat(config.max_blend, 0.0f, 1.0f))
            , m_edgeGuard(ClampFloat(config.edge_guard, 0.0f, 2.0f))
            , m_strengthScale(ClampFloat(config.strength_scale, 0.0f, 1.0f))
            , m_downWidth((m_width + m_factor - 1) / m_factor)
            , m_downHeight((m_height + m_factor - 1) / m_factor)
            , m_blockCols((m_width + m_blockSize - 1) / m_blockSize)
            , m_blockRows((m_height + m_blockSize - 1) / m_blockSize)
            , m_prevFrame(static_cast<std::size_t>(m_width) * m_height * m_channels, 0)
            , m_currFrame(m_prevFrame.size(), 0)
            , m_prevDownLuma(static_cast<std::size_t>(m_downWidth) * m_downHeight, 0)
            , m_currDownLuma(m_prevDownLuma.size(), 0)
            , m_downEdge(m_prevDownLuma.size(), 0.0f)
            , m_downTemporalDiff(m_prevDownLuma.size(), 0.0f)
            , m_downLumaWeight(m_prevDownLuma.size(), 0.0f)
            , m_motion(static_cast<std::size_t>(m_blockCols) * m_blockRows)
        {
        }

        void Reset()
        {
            std::fill(m_prevFrame.begin(), m_prevFrame.end(), std::uint8_t{0});
            std::fill(m_prevDownLuma.begin(), m_prevDownLuma.end(), std::uint8_t{0});
            std::fill(m_downEdge.begin(), m_downEdge.end(), 0.0f);
            std::fill(m_downTemporalDiff.begin(), m_downTemporalDiff.end(), 0.0f);
            std::fill(m_downLumaWeight.begin(), m_downLumaWeight.end(), 0.0f);
            std::fill(m_motion.begin(), m_motion.end(), MotionBlock{});
            m_hasPrev = false;
        }

        int Process(const std::uint8_t* input, std::uint8_t* output, int byteCount)
        {
            if (input == nullptr || output == nullptr || byteCount < static_cast<int>(m_prevFrame.size()))
            {
                return -1;
            }

            std::memcpy(m_currFrame.data(), input, m_prevFrame.size());
            BuildDownscaledLuma(m_currFrame, m_currDownLuma);

            if (!m_hasPrev)
            {
                std::memcpy(output, m_currFrame.data(), m_prevFrame.size());
                std::memcpy(m_prevFrame.data(), output, m_prevFrame.size());
                std::memcpy(m_prevDownLuma.data(), m_currDownLuma.data(), m_currDownLuma.size());
                m_hasPrev = true;
                return 0;
            }

            const float sceneCutError = EstimateSceneChange();
            if (sceneCutError > 0.30f)
            {
                std::memcpy(output, m_currFrame.data(), m_prevFrame.size());
                std::memcpy(m_prevFrame.data(), output, m_prevFrame.size());
                std::memcpy(m_prevDownLuma.data(), m_currDownLuma.data(), m_currDownLuma.size());
                return 0;
            }

            BuildDownscaledGuidance();

            switch (m_algorithm)
            {
            case Algorithm::LumaStabilizer:
            default:
                BlendLumaStabilizer(output, sceneCutError);
                break;
            case Algorithm::FlowGuided:
                BuildFastMotionField(sceneCutError);
                BlendFlowGuided(output, sceneCutError);
                break;
            }

            std::memcpy(m_prevFrame.data(), output, m_prevFrame.size());
            std::memcpy(m_prevDownLuma.data(), m_currDownLuma.data(), m_currDownLuma.size());
            return 0;
        }

    private:
        void BuildDownscaledLuma(const std::vector<std::uint8_t>& frame, std::vector<std::uint8_t>& out)
        {
            for (int dy = 0; dy < m_downHeight; ++dy)
            {
                const int sy0 = dy * m_factor;
                for (int dx = 0; dx < m_downWidth; ++dx)
                {
                    const int sx0 = dx * m_factor;
                    int sum = 0;
                    int count = 0;
                    for (int yy = 0; yy < m_factor; ++yy)
                    {
                        const int sy = sy0 + yy;
                        if (sy >= m_height)
                        {
                            break;
                        }

                        const std::size_t rowBase = static_cast<std::size_t>(sy) * m_width * m_channels;
                        for (int xx = 0; xx < m_factor; ++xx)
                        {
                            const int sx = sx0 + xx;
                            if (sx >= m_width)
                            {
                                break;
                            }

                            const std::size_t index = rowBase + static_cast<std::size_t>(sx) * m_channels;
                            const std::uint8_t b = frame[index + 0];
                            const std::uint8_t g = frame[index + 1];
                            const std::uint8_t r = frame[index + 2];
                            sum += Luma(b, g, r);
                            ++count;
                        }
                    }

                    out[static_cast<std::size_t>(dy) * m_downWidth + dx] = static_cast<std::uint8_t>(count > 0 ? sum / count : 0);
                }
            }
        }

        float EstimateSceneChange() const
        {
            long long sad = 0;
            long long pixels = 0;
            for (std::size_t i = 0; i < m_currDownLuma.size(); ++i)
            {
                sad += std::abs(static_cast<int>(m_currDownLuma[i]) - static_cast<int>(m_prevDownLuma[i]));
                ++pixels;
            }

            return pixels > 0 ? static_cast<float>(sad) / static_cast<float>(pixels * 255.0) : 0.0f;
        }

        void BuildDownscaledGuidance()
        {
            for (int y = 0; y < m_downHeight; ++y)
            {
                const int nextY = ClampInt(y + 1, 0, m_downHeight - 1);
                for (int x = 0; x < m_downWidth; ++x)
                {
                    const int nextX = ClampInt(x + 1, 0, m_downWidth - 1);
                    const std::size_t index = static_cast<std::size_t>(y) * m_downWidth + x;
                    const int center = static_cast<int>(m_currDownLuma[index]);
                    const int right = static_cast<int>(m_currDownLuma[static_cast<std::size_t>(y) * m_downWidth + nextX]);
                    const int down = static_cast<int>(m_currDownLuma[static_cast<std::size_t>(nextY) * m_downWidth + x]);
                    const int prev = static_cast<int>(m_prevDownLuma[index]);
                    m_downEdge[index] = ClampFloat((std::abs(center - right) + std::abs(center - down)) / 255.0f, 0.0f, 1.0f);
                    m_downTemporalDiff[index] = ClampFloat(std::abs(center - prev) / 255.0f, 0.0f, 1.0f);
                }
            }
        }

        void BuildMotionField(float sceneCutError)
        {
            for (int by = 0; by < m_blockRows; ++by)
            {
                for (int bx = 0; bx < m_blockCols; ++bx)
                {
                    MotionBlock motion = EstimateBlockMotion(bx, by);
                    motion.Weight = EstimateBlockWeight(bx, by, motion, sceneCutError);
                    m_motion[static_cast<std::size_t>(by) * m_blockCols + bx] = motion;
                }
            }
        }

        void BuildFastMotionField(float sceneCutError)
        {
            const int fastRadius = std::min(1, m_searchRadius);
            const auto tuning = GetModeTuning();
            for (int by = 0; by < m_blockRows; ++by)
            {
                for (int bx = 0; bx < m_blockCols; ++bx)
                {
                    MotionBlock motion = EstimateBlockMotionFast(bx, by, fastRadius);
                    const int x0 = bx * m_blockSize;
                    const int y0 = by * m_blockSize;
                    const int bw = std::min(m_blockSize, m_width - x0);
                    const int bh = std::min(m_blockSize, m_height - y0);
                    const int centerX = ClampInt(x0 + bw / 2, 0, m_width - 1);
                    const int centerY = ClampInt(y0 + bh / 2, 0, m_height - 1);
                    const float edge = SampleDownscaledEdge(centerX, centerY);
                    const float diff = SampleDownscaledTemporalDiff(centerX, centerY);
                    const float motionMag = ClampFloat((std::abs(motion.Dx) + std::abs(motion.Dy)) / 2.0f, 0.0f, 1.0f);
                    const float stability = 1.0f - ClampFloat(motion.Reprojection * 1.10f + motionMag * 0.45f + diff * 0.60f + sceneCutError * 1.35f, 0.0f, 1.0f);
                    const float protect = 1.0f - ClampFloat(edge * m_edgeGuard * tuning.EdgeGuardScale * 0.92f, 0.0f, 1.0f);
                    motion.Weight = ClampFloat(
                        m_blendStrength * m_strengthScale * stability * protect * tuning.FlickerGain * 0.72f,
                        0.0f,
                        m_maxBlend * m_strengthScale * 0.74f);
                    m_motion[static_cast<std::size_t>(by) * m_blockCols + bx] = motion;
                }
            }
        }

        MotionBlock EstimateBlockMotion(int blockX, int blockY) const
        {
            const int x0 = blockX * m_blockSize;
            const int y0 = blockY * m_blockSize;
            const int bw = std::min(m_blockSize, m_width - x0);
            const int bh = std::min(m_blockSize, m_height - y0);
            const int dsX0 = x0 / m_factor;
            const int dsY0 = y0 / m_factor;
            const int dsW = std::max(1, (bw + m_factor - 1) / m_factor);
            const int dsH = std::max(1, (bh + m_factor - 1) / m_factor);

            int bestDx = 0;
            int bestDy = 0;
            int bestSad = std::numeric_limits<int>::max();

            for (int dy = -m_searchRadius; dy <= m_searchRadius; ++dy)
            {
                for (int dx = -m_searchRadius; dx <= m_searchRadius; ++dx)
                {
                    const int candX0 = dsX0 + dx;
                    const int candY0 = dsY0 + dy;
                    if (candX0 < 0 || candY0 < 0 || candX0 + dsW > m_downWidth || candY0 + dsH > m_downHeight)
                    {
                        continue;
                    }

                    int sad = 0;
                    for (int yy = 0; yy < dsH; ++yy)
                    {
                        const std::size_t currRow = static_cast<std::size_t>(dsY0 + yy) * m_downWidth + dsX0;
                        const std::size_t prevRow = static_cast<std::size_t>(candY0 + yy) * m_downWidth + candX0;
                        for (int xx = 0; xx < dsW; ++xx)
                        {
                            sad += std::abs(static_cast<int>(m_currDownLuma[currRow + xx]) - static_cast<int>(m_prevDownLuma[prevRow + xx]));
                            if (sad >= bestSad)
                            {
                                break;
                            }
                        }

                        if (sad >= bestSad)
                        {
                            break;
                        }
                    }

                    if (sad < bestSad)
                    {
                        bestSad = sad;
                        bestDx = dx;
                        bestDy = dy;
                    }
                }
            }

            MotionBlock result;
            result.Dx = bestDx;
            result.Dy = bestDy;
            return result;
        }

        MotionBlock EstimateBlockMotionFast(int blockX, int blockY, int searchRadius) const
        {
            const int x0 = blockX * m_blockSize;
            const int y0 = blockY * m_blockSize;
            const int bw = std::min(m_blockSize, m_width - x0);
            const int bh = std::min(m_blockSize, m_height - y0);
            const int dsX0 = x0 / m_factor;
            const int dsY0 = y0 / m_factor;
            const int dsW = std::max(1, (bw + m_factor - 1) / m_factor);
            const int dsH = std::max(1, (bh + m_factor - 1) / m_factor);

            int bestDx = 0;
            int bestDy = 0;
            int bestSad = std::numeric_limits<int>::max();

            for (int dy = -searchRadius; dy <= searchRadius; ++dy)
            {
                for (int dx = -searchRadius; dx <= searchRadius; ++dx)
                {
                    const int candX0 = dsX0 + dx;
                    const int candY0 = dsY0 + dy;
                    if (candX0 < 0 || candY0 < 0 || candX0 + dsW > m_downWidth || candY0 + dsH > m_downHeight)
                    {
                        continue;
                    }

                    int sad = 0;
                    for (int yy = 0; yy < dsH; ++yy)
                    {
                        const std::size_t currRow = static_cast<std::size_t>(dsY0 + yy) * m_downWidth + dsX0;
                        const std::size_t prevRow = static_cast<std::size_t>(candY0 + yy) * m_downWidth + candX0;
                        for (int xx = 0; xx < dsW; ++xx)
                        {
                            sad += std::abs(static_cast<int>(m_currDownLuma[currRow + xx]) - static_cast<int>(m_prevDownLuma[prevRow + xx]));
                        }
                    }

                    if (sad < bestSad)
                    {
                        bestSad = sad;
                        bestDx = dx;
                        bestDy = dy;
                    }
                }
            }

            MotionBlock result;
            result.Dx = bestDx;
            result.Dy = bestDy;
            const float area = static_cast<float>(std::max(1, dsW * dsH));
            result.Reprojection = ClampFloat(static_cast<float>(bestSad) / (area * 255.0f), 0.0f, 1.0f);
            return result;
        }

        float EstimateBlockWeight(int blockX, int blockY, const MotionBlock& motion, float sceneCutError) const
        {
            const int x0 = blockX * m_blockSize;
            const int y0 = blockY * m_blockSize;
            const int bw = std::min(m_blockSize, m_width - x0);
            const int bh = std::min(m_blockSize, m_height - y0);
            const int dsX0 = x0 / m_factor;
            const int dsY0 = y0 / m_factor;
            const int dsW = std::max(1, (bw + m_factor - 1) / m_factor);
            const int dsH = std::max(1, (bh + m_factor - 1) / m_factor);

            int sad = 0;
            int edge = 0;
            const int candX0 = ClampInt(dsX0 + motion.Dx, 0, std::max(0, m_downWidth - dsW));
            const int candY0 = ClampInt(dsY0 + motion.Dy, 0, std::max(0, m_downHeight - dsH));

            for (int yy = 0; yy < dsH; ++yy)
            {
                const std::size_t currRow = static_cast<std::size_t>(dsY0 + yy) * m_downWidth + dsX0;
                const std::size_t prevRow = static_cast<std::size_t>(candY0 + yy) * m_downWidth + candX0;
                for (int xx = 0; xx < dsW; ++xx)
                {
                    const int curr = static_cast<int>(m_currDownLuma[currRow + xx]);
                    const int prev = static_cast<int>(m_prevDownLuma[prevRow + xx]);
                    sad += std::abs(curr - prev);
                    if (xx + 1 < dsW)
                    {
                        edge += std::abs(curr - static_cast<int>(m_currDownLuma[currRow + xx + 1]));
                    }
                    if (yy + 1 < dsH)
                    {
                        edge += std::abs(curr - static_cast<int>(m_currDownLuma[currRow + xx + m_downWidth]));
                    }
                }
            }

            const float area = static_cast<float>(std::max(1, dsW * dsH));
            const float reprojection = ClampFloat(static_cast<float>(sad) / (area * 255.0f), 0.0f, 1.0f);
            const float motionMag = ClampFloat((std::abs(motion.Dx) + std::abs(motion.Dy)) / static_cast<float>(m_searchRadius * 2), 0.0f, 1.0f);
            const float edgeMag = ClampFloat(static_cast<float>(edge) / (area * 255.0f * 2.0f), 0.0f, 1.0f);
            const float flatMag = 1.0f - edgeMag;

            const auto tuning = GetModeTuning();
            const float adjustedMotionGate = 1.0f - ClampFloat(motionMag * tuning.MotionGain, 0.0f, 1.0f);
            const float detailPenalty = ClampFloat(edgeMag * m_edgeGuard * tuning.EdgeGuardScale, 0.0f, 1.0f);
            const float sceneGate = 1.0f - ClampFloat(sceneCutError * 2.0f, 0.0f, 1.0f);
            const float tunedFlickerScore = ClampFloat(reprojection * adjustedMotionGate * tuning.FlickerGain * (1.0f + flatMag * tuning.FlatBoost), 0.0f, 1.0f);
            const float weight = m_blendStrength * tunedFlickerScore * (1.0f - detailPenalty) * sceneGate * m_strengthScale;

            return ClampFloat(weight, 0.0f, m_maxBlend * m_strengthScale * tuning.MaxBlendScale);
        }

        float EstimateBlockWeightFast(int blockX, int blockY, const MotionBlock& motion, float sceneCutError) const
        {
            const int x0 = blockX * m_blockSize;
            const int y0 = blockY * m_blockSize;
            const int bw = std::min(m_blockSize, m_width - x0);
            const int bh = std::min(m_blockSize, m_height - y0);
            const int dsX0 = x0 / m_factor;
            const int dsY0 = y0 / m_factor;
            const int dsW = std::max(1, (bw + m_factor - 1) / m_factor);
            const int dsH = std::max(1, (bh + m_factor - 1) / m_factor);

            const int candX0 = ClampInt(dsX0 + motion.Dx, 0, std::max(0, m_downWidth - dsW));
            const int candY0 = ClampInt(dsY0 + motion.Dy, 0, std::max(0, m_downHeight - dsH));

            int sad = 0;
            int edge = 0;
            for (int yy = 0; yy < dsH; ++yy)
            {
                const std::size_t currRow = static_cast<std::size_t>(dsY0 + yy) * m_downWidth + dsX0;
                const std::size_t prevRow = static_cast<std::size_t>(candY0 + yy) * m_downWidth + candX0;
                for (int xx = 0; xx < dsW; ++xx)
                {
                    const int curr = static_cast<int>(m_currDownLuma[currRow + xx]);
                    const int prev = static_cast<int>(m_prevDownLuma[prevRow + xx]);
                    sad += std::abs(curr - prev);
                    if (xx + 1 < dsW)
                    {
                        edge += std::abs(curr - static_cast<int>(m_currDownLuma[currRow + xx + 1]));
                    }
                }
            }

            const float area = static_cast<float>(std::max(1, dsW * dsH));
            const float reprojection = ClampFloat(static_cast<float>(sad) / (area * 255.0f), 0.0f, 1.0f);
            const float edgeMag = ClampFloat(static_cast<float>(edge) / (area * 255.0f), 0.0f, 1.0f);
            const float motionMag = ClampFloat((std::abs(motion.Dx) + std::abs(motion.Dy)) / 2.0f, 0.0f, 1.0f);
            const auto tuning = GetModeTuning();

            const float stability = 1.0f - ClampFloat(reprojection * 1.3f + motionMag * 0.6f + sceneCutError * 1.5f, 0.0f, 1.0f);
            const float protect = 1.0f - ClampFloat(edgeMag * m_edgeGuard * tuning.EdgeGuardScale, 0.0f, 1.0f);
            const float weight = m_blendStrength * m_strengthScale * stability * protect * tuning.MaxBlendScale;
            return ClampFloat(weight, 0.0f, m_maxBlend * m_strengthScale * tuning.MaxBlendScale);
        }

        float SampleDownscaledEdge(int x, int y) const
        {
            const int dsX = ClampInt(x / m_factor, 0, m_downWidth - 1);
            const int dsY = ClampInt(y / m_factor, 0, m_downHeight - 1);
            return m_downEdge[static_cast<std::size_t>(dsY) * m_downWidth + dsX];
        }

        float SampleDownscaledTemporalDiff(int x, int y) const
        {
            const int dsX = ClampInt(x / m_factor, 0, m_downWidth - 1);
            const int dsY = ClampInt(y / m_factor, 0, m_downHeight - 1);
            return m_downTemporalDiff[static_cast<std::size_t>(dsY) * m_downWidth + dsX];
        }

        float SampleDownscaledLumaWeightBilinear(int x, int y) const
        {
            if (m_downWidth == 1 || m_downHeight == 1)
            {
                return m_downLumaWeight.front();
            }

            const float fx = (static_cast<float>(x) + 0.5f) / static_cast<float>(m_factor) - 0.5f;
            const float fy = (static_cast<float>(y) + 0.5f) / static_cast<float>(m_factor) - 0.5f;
            const int x0 = ClampInt(static_cast<int>(std::floor(fx)), 0, m_downWidth - 1);
            const int y0 = ClampInt(static_cast<int>(std::floor(fy)), 0, m_downHeight - 1);
            const int x1 = ClampInt(x0 + 1, 0, m_downWidth - 1);
            const int y1 = ClampInt(y0 + 1, 0, m_downHeight - 1);
            const float tx = ClampFloat(fx - static_cast<float>(x0), 0.0f, 1.0f);
            const float ty = ClampFloat(fy - static_cast<float>(y0), 0.0f, 1.0f);

            const float w00 = m_downLumaWeight[static_cast<std::size_t>(y0) * m_downWidth + x0];
            const float w10 = m_downLumaWeight[static_cast<std::size_t>(y0) * m_downWidth + x1];
            const float w01 = m_downLumaWeight[static_cast<std::size_t>(y1) * m_downWidth + x0];
            const float w11 = m_downLumaWeight[static_cast<std::size_t>(y1) * m_downWidth + x1];

            const float top = Lerp(w00, w10, tx);
            const float bottom = Lerp(w01, w11, tx);
            return Lerp(top, bottom, ty);
        }

        void BlendLumaStabilizer(std::uint8_t* output, float sceneCutError)
        {
            const auto tuning = GetModeTuning();
            std::memcpy(output, m_currFrame.data(), m_currFrame.size());
            for (int dsY = 0; dsY < m_downHeight; ++dsY)
            {
                for (int dsX = 0; dsX < m_downWidth; ++dsX)
                {
                    const std::size_t downIndex = static_cast<std::size_t>(dsY) * m_downWidth + dsX;
                    const float diff = m_downTemporalDiff[downIndex];
                    const float edge = m_downEdge[downIndex];
                    const float flat = 1.0f - edge;
                    const float stability = 1.0f - ClampFloat(diff * 1.6f + sceneCutError * 1.5f, 0.0f, 1.0f);
                    const float detailProtect = 1.0f - ClampFloat(edge * m_edgeGuard * tuning.EdgeGuardScale, 0.0f, 1.0f);
                    const float softenedProtect = detailProtect * detailProtect;
                    m_downLumaWeight[downIndex] = ClampFloat(
                        m_blendStrength * m_strengthScale * stability * (0.52f + flat * (0.25f + tuning.FlatBoost * 0.55f)) * softenedProtect,
                        0.0f,
                        m_maxBlend * m_strengthScale * 0.62f);
                }
            }

            for (int y = 0; y < m_height; ++y)
            {
                const std::size_t rowBase = static_cast<std::size_t>(y) * m_width * m_channels;
                for (int x = 0; x < m_width; ++x)
                {
                    const std::size_t index = rowBase + static_cast<std::size_t>(x) * m_channels;
                    const float localDiff = SampleDownscaledTemporalDiff(x, y);
                    const float localEdge = SampleDownscaledEdge(x, y);
                    const float detailGate = 1.0f - ClampFloat(localEdge * (0.90f + m_edgeGuard * 0.55f), 0.0f, 1.0f);
                    const float flickerGate = 1.0f - ClampFloat(localDiff * 1.35f + sceneCutError * 1.2f, 0.0f, 1.0f);
                    const float weight = ClampFloat(
                        SampleDownscaledLumaWeightBilinear(x, y) * detailGate * flickerGate,
                        0.0f,
                        m_maxBlend * m_strengthScale * 0.56f);

                    const int alpha = ClampInt(static_cast<int>(std::lround(weight * 256.0f)), 0, 256);
                    if (alpha <= 0)
                    {
                        continue;
                    }

                    if (alpha >= 256)
                    {
                        output[index + 0] = m_prevFrame[index + 0];
                        output[index + 1] = m_prevFrame[index + 1];
                        output[index + 2] = m_prevFrame[index + 2];
                    }
                    else
                    {
                        const int invAlpha = 256 - alpha;
                        output[index + 0] = static_cast<std::uint8_t>((static_cast<int>(m_currFrame[index + 0]) * invAlpha + static_cast<int>(m_prevFrame[index + 0]) * alpha + 128) >> 8);
                        output[index + 1] = static_cast<std::uint8_t>((static_cast<int>(m_currFrame[index + 1]) * invAlpha + static_cast<int>(m_prevFrame[index + 1]) * alpha + 128) >> 8);
                        output[index + 2] = static_cast<std::uint8_t>((static_cast<int>(m_currFrame[index + 2]) * invAlpha + static_cast<int>(m_prevFrame[index + 2]) * alpha + 128) >> 8);
                    }

                    if (m_channels == 4)
                    {
                        output[index + 3] = m_currFrame[index + 3];
                    }
                }
            }
        }

        void BlendFlowGuided(std::uint8_t* output, float sceneCutError)
        {
            std::memcpy(output, m_currFrame.data(), m_currFrame.size());
            for (int by = 0; by < m_blockRows; ++by)
            {
                const int y0 = by * m_blockSize;
                const int bh = std::min(m_blockSize, m_height - y0);

                for (int bx = 0; bx < m_blockCols; ++bx)
                {
                    const MotionBlock& motion = m_motion[static_cast<std::size_t>(by) * m_blockCols + bx];
                    const int x0 = bx * m_blockSize;
                    const int bw = std::min(m_blockSize, m_width - x0);
                    const float weight = ClampFloat(
                        motion.Weight * (1.0f - sceneCutError * 0.35f),
                        0.0f,
                        m_maxBlend * m_strengthScale * 0.74f);
                    const int alpha = ClampInt(static_cast<int>(std::lround(weight * 256.0f)), 0, 256);
                    if (alpha <= 0)
                    {
                        continue;
                    }

                    const int dx = motion.Dx * m_factor;
                    const int dy = motion.Dy * m_factor;
                    const int invAlpha = 256 - alpha;

                    for (int yy = 0; yy < bh; ++yy)
                    {
                        const int y = y0 + yy;
                        const int sampleY = ClampInt(y + dy, 0, m_height - 1);
                        const std::size_t rowBase = static_cast<std::size_t>(y) * m_width * m_channels;
                        const std::size_t sampleRowBase = static_cast<std::size_t>(sampleY) * m_width * m_channels;

                        for (int xx = 0; xx < bw; ++xx)
                        {
                            const int x = x0 + xx;
                            const int sampleX = ClampInt(x + dx, 0, m_width - 1);
                            const std::size_t currentIndex = rowBase + static_cast<std::size_t>(x) * m_channels;
                            const std::size_t sampleIndex = sampleRowBase + static_cast<std::size_t>(sampleX) * m_channels;

                            output[currentIndex + 0] = static_cast<std::uint8_t>((static_cast<int>(m_currFrame[currentIndex + 0]) * invAlpha + static_cast<int>(m_prevFrame[sampleIndex + 0]) * alpha + 128) >> 8);
                            output[currentIndex + 1] = static_cast<std::uint8_t>((static_cast<int>(m_currFrame[currentIndex + 1]) * invAlpha + static_cast<int>(m_prevFrame[sampleIndex + 1]) * alpha + 128) >> 8);
                            output[currentIndex + 2] = static_cast<std::uint8_t>((static_cast<int>(m_currFrame[currentIndex + 2]) * invAlpha + static_cast<int>(m_prevFrame[sampleIndex + 2]) * alpha + 128) >> 8);

                            if (m_channels == 4)
                            {
                                output[currentIndex + 3] = m_currFrame[currentIndex + 3];
                            }
                        }
                    }
                }
            }
        }

        void BlendEdgeClamp(std::uint8_t* output, float sceneCutError)
        {
            const auto tuning = GetModeTuning();
            for (int y = 0; y < m_height; ++y)
            {
                for (int x = 0; x < m_width; ++x)
                {
                    const std::size_t index = (static_cast<std::size_t>(y) * m_width + x) * m_channels;
                    const float diff = SampleDownscaledTemporalDiff(x, y);
                    const float edge = SampleDownscaledEdge(x, y);
                    const float flat = 1.0f - edge;
                    const float sceneGate = 1.0f - ClampFloat(sceneCutError * 1.8f, 0.0f, 1.0f);
                    const float clampWeight = ClampFloat(
                        m_blendStrength * m_strengthScale * sceneGate *
                        (1.0f - ClampFloat(diff * 1.25f, 0.0f, 1.0f)) *
                        (1.0f - ClampFloat(edge * m_edgeGuard * tuning.EdgeGuardScale, 0.0f, 1.0f)) *
                        (0.85f + flat * tuning.FlatBoost),
                        0.0f,
                        m_maxBlend * m_strengthScale * 1.05f);

                    const int clampRadius = ClampInt(static_cast<int>(std::lround(4.0f + edge * 16.0f + diff * 8.0f)), 2, 28);

                    for (int c = 0; c < 3; ++c)
                    {
                        const int curr = static_cast<int>(m_currFrame[index + c]);
                        const int prev = static_cast<int>(m_prevFrame[index + c]);
                        const int lo = std::max(0, prev - clampRadius);
                        const int hi = std::min(255, prev + clampRadius);
                        const float clamped = static_cast<float>(ClampInt(curr, lo, hi));
                        output[index + c] = static_cast<std::uint8_t>(std::lround(curr * (1.0f - clampWeight) + clamped * clampWeight));
                    }

                    if (m_channels == 4)
                    {
                        output[index + 3] = m_currFrame[index + 3];
                    }
                }
            }
        }

        void BlendFrame(std::uint8_t* output)
        {
            if (m_channels != 3 && m_channels != 4)
            {
                std::memcpy(output, m_currFrame.data(), m_currFrame.size());
                return;
            }

            for (int y = 0; y < m_height; ++y)
            {
                const int by = std::min(y / m_blockSize, m_blockRows - 1);
                const int by1 = std::min(by + 1, m_blockRows - 1);
                const float ty = m_blockSize > 1 ? static_cast<float>(y - by * m_blockSize) / static_cast<float>(m_blockSize) : 0.0f;

                for (int x = 0; x < m_width; ++x)
                {
                    const int bx = std::min(x / m_blockSize, m_blockCols - 1);
                    const int bx1 = std::min(bx + 1, m_blockCols - 1);
                    const float tx = m_blockSize > 1 ? static_cast<float>(x - bx * m_blockSize) / static_cast<float>(m_blockSize) : 0.0f;

                    const MotionBlock& m00 = m_motion[static_cast<std::size_t>(by) * m_blockCols + bx];
                    const MotionBlock& m10 = m_motion[static_cast<std::size_t>(by) * m_blockCols + bx1];
                    const MotionBlock& m01 = m_motion[static_cast<std::size_t>(by1) * m_blockCols + bx];
                    const MotionBlock& m11 = m_motion[static_cast<std::size_t>(by1) * m_blockCols + bx1];

                    const float topX = Lerp(static_cast<float>(m00.Dx), static_cast<float>(m10.Dx), tx);
                    const float botX = Lerp(static_cast<float>(m01.Dx), static_cast<float>(m11.Dx), tx);
                    const float topY = Lerp(static_cast<float>(m00.Dy), static_cast<float>(m10.Dy), tx);
                    const float botY = Lerp(static_cast<float>(m01.Dy), static_cast<float>(m11.Dy), tx);
                    const float motionX = Lerp(topX, botX, ty);
                    const float motionY = Lerp(topY, botY, ty);

                    const float topW = Lerp(m00.Weight, m10.Weight, tx);
                    const float botW = Lerp(m01.Weight, m11.Weight, tx);
                    const float weight = ClampFloat(Lerp(topW, botW, ty), 0.0f, m_maxBlend);

                    const int sampleX = ClampInt(static_cast<int>(std::lround(x + motionX * m_factor)), 0, m_width - 1);
                    const int sampleY = ClampInt(static_cast<int>(std::lround(y + motionY * m_factor)), 0, m_height - 1);

                    const std::size_t currentIndex = (static_cast<std::size_t>(y) * m_width + x) * m_channels;
                    const std::size_t sampleIndex = (static_cast<std::size_t>(sampleY) * m_width + sampleX) * m_channels;

                    for (int c = 0; c < 3; ++c)
                    {
                        const float curr = static_cast<float>(m_currFrame[currentIndex + c]);
                        const float prev = static_cast<float>(m_prevFrame[sampleIndex + c]);
                        output[currentIndex + c] = static_cast<std::uint8_t>(std::lround(curr * (1.0f - weight) + prev * weight));
                    }

                    if (m_channels == 4)
                    {
                        output[currentIndex + 3] = m_currFrame[currentIndex + 3];
                    }
                }
            }
        }

        static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }

        ModeTuning GetModeTuning() const
        {
            switch (m_contentMode)
            {
            case ContentMode::Video:
                return { 0.88f, 1.00f, 0.92f, 0.88f, 0.10f };
            case ContentMode::Faces:
                return { 0.62f, 0.80f, 1.38f, 0.62f, 0.04f };
            case ContentMode::AnimeUltra:
                return { 1.48f, 0.70f, 1.50f, 1.18f, 0.34f };
            case ContentMode::Anime:
            default:
                return { 1.30f, 0.78f, 1.34f, 1.08f, 0.22f };
            }
        }

        const int m_width;
        const int m_height;
        const int m_channels;
        const int m_factor;
        const int m_blockSize;
        const int m_searchRadius;
        const ContentMode m_contentMode;
        const Algorithm m_algorithm;
        const float m_blendStrength;
        const float m_maxBlend;
        const float m_edgeGuard;
        const float m_strengthScale;
        const int m_downWidth;
        const int m_downHeight;
        const int m_blockCols;
        const int m_blockRows;

        bool m_hasPrev = false;
        std::vector<std::uint8_t> m_prevFrame;
        std::vector<std::uint8_t> m_currFrame;
        std::vector<std::uint8_t> m_prevDownLuma;
        std::vector<std::uint8_t> m_currDownLuma;
        std::vector<float> m_downEdge;
        std::vector<float> m_downTemporalDiff;
        std::vector<float> m_downLumaWeight;
        std::vector<MotionBlock> m_motion;
    };
}

AF_API void* af_create(const af_config* config)
{
    if (config == nullptr || config->width <= 0 || config->height <= 0 || (config->channels != 3 && config->channels != 4))
    {
        return nullptr;
    }

    try
    {
        return new AntiFlickerContext(*config);
    }
    catch (...)
    {
        return nullptr;
    }
}

AF_API void af_destroy(void* handle)
{
    delete static_cast<AntiFlickerContext*>(handle);
}

AF_API void af_reset(void* handle)
{
    if (handle == nullptr)
    {
        return;
    }

    static_cast<AntiFlickerContext*>(handle)->Reset();
}

AF_API int af_process(void* handle, const std::uint8_t* input_bgr, std::uint8_t* output_bgr, int byte_count)
{
    if (handle == nullptr)
    {
        return -1;
    }

    return static_cast<AntiFlickerContext*>(handle)->Process(input_bgr, output_bgr, byte_count);
}
